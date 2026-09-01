using MTranslate.Core;
using MTranslate.Infrastructure;

namespace MTranslate.Infrastructure.Tests;

public sealed class SqliteTranslationHistoryStoreTests
{
    [Fact]
    public async Task AddSearchDeleteAndReopen_PreserveHistory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mtranslate-history-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "app.db");
        var id = Guid.NewGuid();
        try
        {
            await using (var store = new SqliteTranslationHistoryStore(path))
            {
                await store.AddAsync(new TranslationHistoryEntry(
                    id, "Hello world", "你好，世界", "en", "zh-CN", "hy-mt2-1.8b-q4",
                    DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(420)));
            }

            await using var reopened = new SqliteTranslationHistoryStore(path);
            var all = await reopened.SearchAsync();
            var searched = await reopened.SearchAsync("世界");

            Assert.Single(all);
            Assert.Equal(id, all[0].Id);
            Assert.Single(searched);
            Assert.True(await reopened.DeleteAsync(id));
            Assert.Empty(await reopened.SearchAsync());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Clear_RemovesEveryHistoryEntry()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mtranslate-history-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "app.db");
        try
        {
            await using var store = new SqliteTranslationHistoryStore(path);
            for (var index = 0; index < 2; index++)
            {
                await store.AddAsync(new TranslationHistoryEntry(
                    Guid.NewGuid(), $"source-{index}", $"target-{index}", "en", "zh-CN", "model",
                    DateTimeOffset.UtcNow.AddSeconds(index), TimeSpan.FromSeconds(1)));
            }

            await store.ClearAsync();

            Assert.Empty(await store.SearchAsync());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
