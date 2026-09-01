using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace MTranslate.Api;

public sealed class SqliteApiClientStore : IApiClientStore, IAsyncDisposable
{
    private readonly string databasePath;
    private readonly string connectionString;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;

    public SqliteApiClientStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        this.databasePath = Path.GetFullPath(databasePath);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = this.databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
    }

    public async Task<IssuedApiClient> IssueAsync(
        string name,
        string clientType,
        string permissions = "translate",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            throw new ArgumentException("Client name must contain 1 to 100 characters.", nameof(name));
        if (string.IsNullOrWhiteSpace(clientType) || clientType.Trim().Length > 50)
            throw new ArgumentException("Client type must contain 1 to 50 characters.", nameof(clientType));

        var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        var client = new ApiClient(Guid.NewGuid(), name.Trim(), clientType.Trim(), now, null, permissions, false);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedUnsafeAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ApiClient (Id, Name, ClientType, TokenHash, CreatedAt, LastUsedAt, Permissions, Revoked)
                VALUES ($id, $name, $clientType, $tokenHash, $createdAt, NULL, $permissions, 0)
                """;
            command.Parameters.AddWithValue("$id", client.Id.ToString("D"));
            command.Parameters.AddWithValue("$name", client.Name);
            command.Parameters.AddWithValue("$clientType", client.ClientType);
            command.Parameters.AddWithValue("$tokenHash", Hash(token));
            command.Parameters.AddWithValue("$createdAt", client.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$permissions", client.Permissions);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
        return new IssuedApiClient(client, token);
    }

    public async Task<ApiClient?> ValidateAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedUnsafeAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var select = connection.CreateCommand();
            select.Transaction = (SqliteTransaction)transaction;
            select.CommandText = """
                SELECT Id, Name, ClientType, CreatedAt, LastUsedAt, Permissions, Revoked
                FROM ApiClient WHERE TokenHash = $tokenHash AND Revoked = 0
                """;
            select.Parameters.AddWithValue("$tokenHash", Hash(token));
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;
            var client = ReadClient(reader);
            await reader.DisposeAsync().ConfigureAwait(false);

            var now = DateTimeOffset.UtcNow;
            await using var update = connection.CreateCommand();
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = "UPDATE ApiClient SET LastUsedAt = $now WHERE Id = $id";
            update.Parameters.AddWithValue("$now", now.ToString("O"));
            update.Parameters.AddWithValue("$id", client.Id.ToString("D"));
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return client with { LastUsedAt = now };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ApiClient>> ListAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedUnsafeAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, Name, ClientType, CreatedAt, LastUsedAt, Permissions, Revoked
                FROM ApiClient ORDER BY CreatedAt DESC
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var clients = new List<ApiClient>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                clients.Add(ReadClient(reader));
            return clients;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedUnsafeAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE ApiClient SET Revoked = 1 WHERE Id = $id AND Revoked = 0";
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        }
        finally
        {
            gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task EnsureInitializedUnsafeAsync(CancellationToken cancellationToken)
    {
        if (initialized)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ApiClient (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                ClientType TEXT NOT NULL,
                TokenHash TEXT NOT NULL UNIQUE,
                CreatedAt TEXT NOT NULL,
                LastUsedAt TEXT NULL,
                Permissions TEXT NOT NULL,
                Revoked INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS IX_ApiClient_TokenHash ON ApiClient(TokenHash);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        initialized = true;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static ApiClient ReadClient(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        reader.GetString(2),
        DateTimeOffset.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture),
        reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture),
        reader.GetString(5),
        reader.GetInt64(6) != 0);

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
