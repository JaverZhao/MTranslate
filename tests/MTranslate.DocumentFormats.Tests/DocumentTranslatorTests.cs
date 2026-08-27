using System.Runtime.CompilerServices;
using MTranslate.Core;

namespace MTranslate.DocumentFormats.Tests;

public sealed class DocumentTranslatorTests
{
    [Fact]
    public async Task TranslateAsync_WritesValidatedOutputAtomicallyAndReportsTokenProgress()
    {
        var directory = CreateDirectory();
        try
        {
            var input = Path.Combine(directory, "sample.srt");
            var output = Path.Combine(directory, "sample.zh-CN.srt");
            await File.WriteAllTextAsync(input, "1\n00:00:01,000 --> 00:00:02,000\nHello.\n\n2\n00:00:03,000 --> 00:00:04,000\nWorld.\n");
            await using var queue = new TranslationJobQueue();
            var translator = CreateTranslator(directory, queue, new PrefixTranslationClient());
            var updates = new List<DocumentTranslationProgress>();

            var result = await translator.TranslateAsync(
                new DocumentTranslationRequest(input, output, "zh-CN", "en"),
                new SynchronousProgress<DocumentTranslationProgress>(updates.Add));

            Assert.True(File.Exists(output));
            Assert.False(File.Exists(output + ".tmp"));
            Assert.Equal(2, result.SegmentCount);
            Assert.Equal(100, updates[^1].Percentage);
            var written = await File.ReadAllTextAsync(output);
            Assert.Contains("译:Hello.", written);
            Assert.Contains("00:00:03,000 --> 00:00:04,000", written);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TranslateAsync_ResumesCompletedSegmentsFromCheckpoint()
    {
        var directory = CreateDirectory();
        try
        {
            var input = Path.Combine(directory, "sample.txt");
            var output = Path.Combine(directory, "sample.zh-CN.txt");
            await File.WriteAllTextAsync(input, "First paragraph.\n\nSecond paragraph.");
            var jobId = Guid.NewGuid();
            var failingClient = new FailOnSecondRequestClient();
            await using (var firstQueue = new TranslationJobQueue())
            {
                var firstTranslator = CreateTranslator(directory, firstQueue, failingClient);
                await Assert.ThrowsAsync<InvalidOperationException>(() => firstTranslator.TranslateAsync(
                    new DocumentTranslationRequest(input, output, "zh-CN", "en", JobId: jobId)));
            }

            var resumedClient = new PrefixTranslationClient();
            await using (var secondQueue = new TranslationJobQueue())
            {
                var secondTranslator = CreateTranslator(directory, secondQueue, resumedClient);
                var result = await secondTranslator.TranslateAsync(
                    new DocumentTranslationRequest(input, output, "zh-CN", "en", JobId: jobId));

                Assert.True(result.ResumedFromCheckpoint);
                Assert.Single(resumedClient.Requests);
                Assert.Contains("译:First paragraph.", await File.ReadAllTextAsync(output));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TranslateAsync_AutoDetectsTurkishOnceForAllTxtLines()
    {
        var directory = CreateDirectory();
        try
        {
            var input = Path.Combine(directory, "turkish.txt");
            var output = Path.Combine(directory, "turkish.zh-CN.txt");
            await File.WriteAllTextAsync(input,
                "Bugün biraz yorgun musun?\nCatchii'ye gel.\nHadi tanışalım ve biraz sohbet edelim.");
            var client = new PrefixTranslationClient();
            await using var queue = new TranslationJobQueue();
            var translator = CreateTranslator(directory, queue, client);

            await translator.TranslateAsync(new DocumentTranslationRequest(input, output, "zh-CN"));

            Assert.Equal(3, client.Requests.Count);
            Assert.All(client.Requests, request => Assert.Equal("tr", request.SourceLanguage));
            Assert.Equal(3, (await File.ReadAllLinesAsync(output)).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static DocumentTranslator CreateTranslator(string directory, TranslationJobQueue queue, ITranslationClient client)
    {
        var service = new TranslationService(client, new ChunkManager(), new NullTranslationCache(), queue);
        return new DocumentTranslator(service, new DocumentParserRegistry(), new FileDocumentCheckpointStore(Path.Combine(directory, "checkpoints")));
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mtranslate-documents-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class PrefixTranslationClient : ITranslationClient
    {
        public List<TranslationRequest> Requests { get; } = [];
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var text = request.Text.Contains("<mtranslate-segment", StringComparison.Ordinal)
                ? request.Text.Replace("Hello.", "译:Hello.", StringComparison.Ordinal).Replace("World.", "译:World.", StringComparison.Ordinal)
                : "译:" + request.Text;
            return Task.FromResult(new TranslationResult(text, TimeSpan.Zero, TimeSpan.Zero, null, null));
        }
        public async IAsyncEnumerable<TranslationChunk> TranslateStreamingAsync(TranslationRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new TranslationChunk(request.Text);
        }
    }

    private sealed class FailOnSecondRequestClient : ITranslationClient
    {
        private int count;
        public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
        {
            count++;
            if (count == 2)
                throw new InvalidOperationException("Synthetic translation failure.");
            return Task.FromResult(new TranslationResult("译:" + request.Text, TimeSpan.Zero, TimeSpan.Zero, null, null));
        }
        public async IAsyncEnumerable<TranslationChunk> TranslateStreamingAsync(TranslationRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new TranslationChunk(request.Text);
        }
    }

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
