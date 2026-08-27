using Microsoft.Data.Sqlite;
using MTranslate.Core;

namespace MTranslate.Infrastructure;

public sealed class SqliteTranslationCache : ITranslationCache, IAsyncDisposable
{
    private readonly string databasePath;
    private readonly string connectionString;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;

    public SqliteTranslationCache(string databasePath)
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

    public bool Enabled { get; set; } = true;

    public async Task<string?> TryGetAsync(TranslationCacheKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!Enabled)
            return null;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedUnsafeAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var hash = key.ComputeHash();
            await using var select = connection.CreateCommand();
            select.Transaction = (SqliteTransaction)transaction;
            select.CommandText = "SELECT TranslatedText FROM TranslationCache WHERE Hash = $hash";
            select.Parameters.AddWithValue("$hash", hash);
            var translated = await select.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (translated is null)
                return null;

            await using var update = connection.CreateCommand();
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = "UPDATE TranslationCache SET LastUsedAt = $now, HitCount = HitCount + 1 WHERE Hash = $hash";
            update.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            update.Parameters.AddWithValue("$hash", hash);
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return translated;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SetAsync(TranslationCacheKey key, string translatedText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(translatedText);
        if (!Enabled)
            return;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedUnsafeAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO TranslationCache
                    (Hash, SourceLanguage, TargetLanguage, Model, SourceText, TranslatedText, CreatedAt, LastUsedAt, HitCount)
                VALUES
                    ($hash, $source, $target, $model, $sourceText, $translatedText, $now, $now, 0)
                ON CONFLICT(Hash) DO UPDATE SET
                    TranslatedText = excluded.TranslatedText,
                    LastUsedAt = excluded.LastUsedAt
                """;
            var now = DateTimeOffset.UtcNow.ToString("O");
            command.Parameters.AddWithValue("$hash", key.ComputeHash());
            command.Parameters.AddWithValue("$source", key.SourceLanguage ?? "auto");
            command.Parameters.AddWithValue("$target", key.TargetLanguage);
            command.Parameters.AddWithValue("$model", key.ModelProfile);
            command.Parameters.AddWithValue("$sourceText", key.SourceText);
            command.Parameters.AddWithValue("$translatedText", translatedText);
            command.Parameters.AddWithValue("$now", now);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
            command.CommandText = "DELETE FROM TranslationCache; VACUUM;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task TrimAsync(long maximumBytes = 500L * 1024 * 1024, CancellationToken cancellationToken = default)
    {
        if (maximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedUnsafeAsync(cancellationToken).ConfigureAwait(false);
            if (!File.Exists(databasePath) || new FileInfo(databasePath).Length <= maximumBytes)
                return;

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            while (new FileInfo(databasePath).Length > maximumBytes)
            {
                await using var delete = connection.CreateCommand();
                delete.CommandText = """
                    DELETE FROM TranslationCache WHERE Hash IN
                    (SELECT Hash FROM TranslationCache ORDER BY LastUsedAt ASC LIMIT 100)
                    """;
                var removed = await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                if (removed == 0)
                    break;
                await using var vacuum = connection.CreateCommand();
                vacuum.CommandText = "VACUUM";
                await vacuum.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
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
            CREATE TABLE IF NOT EXISTS TranslationCache (
                Hash TEXT PRIMARY KEY,
                SourceLanguage TEXT NOT NULL,
                TargetLanguage TEXT NOT NULL,
                Model TEXT NOT NULL,
                SourceText TEXT NOT NULL,
                TranslatedText TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                LastUsedAt TEXT NOT NULL,
                HitCount INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS IX_TranslationCache_LastUsedAt ON TranslationCache(LastUsedAt);
            PRAGMA user_version = 1;
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
}
