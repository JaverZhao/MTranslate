using System.Net;
using System.Security.Cryptography;
using MTranslate.Infrastructure;
using Xunit;

namespace MTranslate.Infrastructure.Tests;

public sealed class ResumableModelDownloaderTests
{
    [Fact]
    public async Task DownloadAsync_VerifiesHashAndAtomicallyMovesFile()
    {
        var content = "test model bytes"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var handler = new ByteArrayHttpMessageHandler(content);
        using var client = new HttpClient(handler);
        var downloader = new ResumableModelDownloader(client);
        var directory = Directory.CreateTempSubdirectory("mtranslate-tests-");
        var destination = Path.Combine(directory.FullName, "model.gguf");

        try
        {
            await downloader.DownloadAsync(new Uri("https://example.test/model.gguf"), destination, hash);

            Assert.Equal(content, await File.ReadAllBytesAsync(destination));
            Assert.False(File.Exists(destination + ".part"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAsync_OnHashMismatch_RemovesInvalidPartialFile()
    {
        var content = "corrupt model bytes"u8.ToArray();
        var handler = new ByteArrayHttpMessageHandler(content);
        using var client = new HttpClient(handler);
        var downloader = new ResumableModelDownloader(client);
        var directory = Directory.CreateTempSubdirectory("mtranslate-tests-");
        var destination = Path.Combine(directory.FullName, "model.gguf");

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(
                new Uri("https://example.test/model.gguf"),
                destination,
                new string('0', 64)));

            Assert.False(File.Exists(destination));
            Assert.False(File.Exists(destination + ".part"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAsync_WithPartialFile_RequestsRemainingRange()
    {
        var content = "resumable model bytes"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var handler = new ByteArrayHttpMessageHandler(content);
        using var client = new HttpClient(handler);
        var downloader = new ResumableModelDownloader(client);
        var directory = Directory.CreateTempSubdirectory("mtranslate-tests-");
        var destination = Path.Combine(directory.FullName, "model.gguf");
        await File.WriteAllBytesAsync(destination + ".part", content[..7]);

        try
        {
            await downloader.DownloadAsync(new Uri("https://example.test/model.gguf"), destination, hash);

            Assert.Equal(7, handler.LastRangeStart);
            Assert.Equal(content, await File.ReadAllBytesAsync(destination));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class ByteArrayHttpMessageHandler(byte[] content) : HttpMessageHandler
    {
        public long? LastRangeStart { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var start = request.Headers.Range?.Ranges.Single().From ?? 0;
            LastRangeStart = request.Headers.Range is null ? null : start;
            var responseContent = content[(int)start..];
            var response = new HttpResponseMessage(start > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(responseContent)
            };
            response.Content.Headers.ContentLength = responseContent.Length;
            return Task.FromResult(response);
        }
    }
}
