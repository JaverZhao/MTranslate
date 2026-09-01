using MTranslate.Core;

namespace MTranslate.Core.Tests;

public sealed class TranslationServiceTests
{
    [Fact]
    public async Task TranslateAsync_ChunksCachesAndReusesTranslations()
    {
        var client = new RecordingClient();
        var cache = new MemoryCache();
        await using var queue = new TranslationJobQueue();
        var service = new TranslationService(client, new ChunkManager(new CharacterEstimator()), cache, queue);
        var request = new TranslationServiceRequest(
            "one two three four five six",
            "zh",
            ModelProfile: "fast",
            Chunking: new ChunkingOptions(8, 12));

        var first = await service.TranslateAsync(request);
        var second = await service.TranslateAsync(request);

        Assert.True(first.ChunkCount > 1);
        Assert.Equal(first.ChunkCount, client.Requests.Count);
        Assert.Equal(first.Text, second.Text);
        Assert.Equal(second.ChunkCount, second.CacheHits);
        Assert.Contains(client.Requests.Skip(1), item => item.Context is not null);
    }

    [Fact]
    public async Task TranslateAsync_ContextParticipatesInCacheIdentityAndCacheCanBeBypassed()
    {
        var client = new RecordingClient();
        var cache = new MemoryCache();
        await using var queue = new TranslationJobQueue();
        var service = new TranslationService(client, new ChunkManager(), cache, queue);

        await service.TranslateAsync(new TranslationServiceRequest("bank", "zh-CN", "en", Context: "river bank"));
        await service.TranslateAsync(new TranslationServiceRequest("bank", "zh-CN", "en", Context: "financial bank"));
        await service.TranslateAsync(new TranslationServiceRequest("bank", "zh-CN", "en", Context: "river bank"));
        await service.TranslateAsync(new TranslationServiceRequest("bank", "zh-CN", "en", Context: "river bank", UseCache: false));

        Assert.Equal(3, client.Requests.Count);
        Assert.Equal("river bank", client.Requests[0].Context);
        Assert.Equal("financial bank", client.Requests[1].Context);
    }

    private sealed class CharacterEstimator : ITokenEstimator
    {
        public int Estimate(string text) => text.Length;
    }

    private sealed class RecordingClient : ITranslationClient
    {
        public List<TranslationRequest> Requests { get; } = [];

        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new TranslationResult(
                $"[{request.Text}]", TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2), 3, 4));
        }

        public async IAsyncEnumerable<TranslationChunk> TranslateStreamingAsync(
            TranslationRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new TranslationChunk(request.Text);
        }
    }

    private sealed class MemoryCache : ITranslationCache
    {
        private readonly Dictionary<string, string> entries = [];
        public bool Enabled { get; set; } = true;
        public Task<string?> TryGetAsync(TranslationCacheKey key, CancellationToken cancellationToken = default) =>
            Task.FromResult(entries.GetValueOrDefault(key.ComputeHash()));
        public Task SetAsync(TranslationCacheKey key, string translatedText, CancellationToken cancellationToken = default)
        {
            entries[key.ComputeHash()] = translatedText;
            return Task.CompletedTask;
        }
        public Task ClearAsync(CancellationToken cancellationToken = default) { entries.Clear(); return Task.CompletedTask; }
        public Task TrimAsync(long maximumBytes = 500L * 1024 * 1024, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
