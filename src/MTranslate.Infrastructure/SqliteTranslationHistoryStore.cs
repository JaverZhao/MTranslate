using Microsoft.Data.Sqlite;
using MTranslate.Core;

namespace MTranslate.Infrastructure;

public sealed class SqliteTranslationHistoryStore : ITranslationHistoryStore, IAsyncDisposable
{
    private readonly string databasePath;
    private readonly string connectionString;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;

    public SqliteTranslationHistoryStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        this.databasePath = Path.GetFullPath(databasePath);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = this.databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
    }

    public async Task AddAsync(TranslationHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedUnsafeAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO TranslationHistory
                    (Id, SourceText, TranslatedText, SourceLanguage, TargetLanguage, ModelId, CreatedAt, ElapsedMilliseconds)
                VALUES
                    ($id, $sourceText, $translatedText, $sourceLanguage, $targetLanguage, $modelId, $createdAt, $elapsedMilliseconds)
                """;
            command.Parameters.AddWithValue("$id", entry.Id.ToString("D"));
            command.Parameters.AddWithValue("$sourceText", entry.SourceText);
            command.Parameters.AddWithValue("$translatedText", entry.TranslatedText);
            command.Parameters.AddWithValue("$sourceLanguage", entry.SourceLanguage);
            command.Parameters.AddWithValue("$targetLanguage", entry.TargetLanguage);
            command.Parameters.AddWithValue("$modelId", entry.ModelId);
            command.Parameters.AddWithValue("$createdAt", entry.CreatedAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$elapsedMilliseconds", entry.Elapsed.TotalMilliseconds);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<TranslationHistoryEntry>> SearchAsync(
        string? query = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(limit));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedUnsafeAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            var hasQuery = !string.IsNullOrWhiteSpace(query);
            command.CommandText = hasQuery
                ? """
                    SELECT Id, SourceText, TranslatedText, SourceLanguage, TargetLanguage, ModelId, CreatedAt, ElapsedMilliseconds
                    FROM TranslationHistory
                    WHERE SourceText LIKE $query ESCAPE '\' OR TranslatedText LIKE $query ESCAPE '\'
                    ORDER BY CreatedAt DESC
                    LIMIT $limit
                    """
                : """
                    SELECT Id, SourceText, TranslatedText, SourceLanguage, TargetLanguage, ModelId, CreatedAt, ElapsedMilliseconds
                    FROM TranslationHistory
                    ORDER BY CreatedAt DESC
                    LIMIT $limit
                    """;
            if (hasQuery)
                command.Parameters.AddWithValue("$query", $"%{EscapeLike(query!.Trim())}%");
            command.Parameters.AddWithValue("$limit", limit);

            var entries = new List<TranslationHistoryEntry>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                entries.Add(new TranslationHistoryEntry(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    DateTimeOffset.Parse(reader.GetString(6), System.Globalization.CultureInfo.InvariantCulture),
                    TimeSpan.FromMilliseconds(reader.GetDouble(7))));
            }
            return entries;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedUnsafeAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM TranslationHistory WHERE Id = $id";
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedUnsafeAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM TranslationHistory";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
            CREATE TABLE IF NOT EXISTS TranslationHistory (
                Id TEXT PRIMARY KEY,
                SourceText TEXT NOT NULL,
                TranslatedText TEXT NOT NULL,
                SourceLanguage TEXT NOT NULL,
                TargetLanguage TEXT NOT NULL,
                ModelId TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                ElapsedMilliseconds REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_TranslationHistory_CreatedAt ON TranslationHistory(CreatedAt DESC);
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

    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);
}
