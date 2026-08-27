using Microsoft.Data.Sqlite;
using MTranslate.Core;
using MTranslate.Infrastructure;

namespace MTranslate.Infrastructure.Tests;

public sealed class SqliteTranslationCacheTests
{
    [Fact]
    public async Task SetAndGet_PersistsTranslationAndUpdatesHitCount()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mtranslate-cache-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "app.db");
        try
        {
            await using var cache = new SqliteTranslationCache(path);
            var key = new TranslationCacheKey("Hello", "en", "zh", "fast");

            await cache.SetAsync(key, "你好");
            Assert.Equal("你好", await cache.TryGetAsync(key));

            await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT HitCount FROM TranslationCache WHERE Hash = $hash";
            command.Parameters.AddWithValue("$hash", key.ComputeHash());
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DisabledCache_DoesNotReadOrWrite()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mtranslate-cache-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "app.db");
        try
        {
            await using var cache = new SqliteTranslationCache(path) { Enabled = false };
            var key = new TranslationCacheKey("Hello", "en", "zh", "fast");

            await cache.SetAsync(key, "你好");

            Assert.Null(await cache.TryGetAsync(key));
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ClearAsync_RemovesAllEntries()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mtranslate-cache-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "app.db");
        try
        {
            await using var cache = new SqliteTranslationCache(path);
            var key = new TranslationCacheKey("Hello", "en", "zh", "fast");
            await cache.SetAsync(key, "你好");

            await cache.ClearAsync();

            Assert.Null(await cache.TryGetAsync(key));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
