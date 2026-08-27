using System.Net;
using System.Text;
using MTranslate.Core;
using MTranslate.Infrastructure;
using Xunit;

namespace MTranslate.Infrastructure.Tests;

public sealed class LlamaServerTranslationClientTests
{
    [Fact]
    public async Task TranslateAsync_ParsesTextAndUsage()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal("http://localhost/v1/chat/completions", request.RequestUri?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"choices":[{"message":{"content":"你好"}}],"usage":{"prompt_tokens":12,"completion_tokens":2}}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new LlamaServerTranslationClient(httpClient, new TranslationPromptBuilder());

        var result = await client.TranslateAsync(new TranslationRequest("Hello", "Chinese"));

        Assert.Equal("你好", result.Text);
        Assert.Equal(12, result.PromptTokens);
        Assert.Equal(2, result.CompletionTokens);
    }

    [Fact]
    public async Task TranslateStreamingAsync_CombinesServerSentEventChunks()
    {
        const string events = "data: {\"choices\":[{\"delta\":{\"content\":\"你\"}}]}\n\n" +
                              "data: {\"choices\":[{\"delta\":{\"content\":\"好\"}}]}\n\n" +
                              "data: [DONE]\n\n";
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(events, Encoding.UTF8, "text/event-stream")
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new LlamaServerTranslationClient(httpClient, new TranslationPromptBuilder());
        var output = new StringBuilder();

        await foreach (var chunk in client.TranslateStreamingAsync(new TranslationRequest("Hello", "Chinese")))
            output.Append(chunk.Text);

        Assert.Equal("你好", output.ToString());
    }

    [Fact]
    public async Task TranslateAsync_OnServerFailure_IncludesResponseBody()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("model failed")
        });
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var client = new LlamaServerTranslationClient(httpClient, new TranslationPromptBuilder());

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.TranslateAsync(new TranslationRequest("Hello", "Chinese")));

        Assert.Contains("model failed", exception.Message, StringComparison.Ordinal);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }
}
