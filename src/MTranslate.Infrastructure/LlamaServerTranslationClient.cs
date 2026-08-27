using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MTranslate.Core;

namespace MTranslate.Infrastructure;

public sealed class LlamaServerTranslationClient(
    HttpClient httpClient,
    ITranslationPromptBuilder promptBuilder) : ITranslationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        var profile = request.Profile ?? TranslationProfile.Default;
        profile.Validate();
        var payload = CreatePayload(request, profile, stream: false);
        var stopwatch = Stopwatch.StartNew();

        using var response = await httpClient.PostAsJsonAsync(
            "v1/chat/completions",
            payload,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadFromJsonAsync<CompletionResponse>(
            JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("llama-server returned an empty JSON response.");
        var text = body.Choices.FirstOrDefault()?.Message?.Content;
        if (text is null)
            throw new InvalidDataException("llama-server response did not contain translated text.");

        stopwatch.Stop();
        return new TranslationResult(
            text,
            stopwatch.Elapsed,
            stopwatch.Elapsed,
            body.Usage?.PromptTokens,
            body.Usage?.CompletionTokens);
    }

    public async IAsyncEnumerable<TranslationChunk> TranslateStreamingAsync(
        TranslationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var profile = request.Profile ?? TranslationProfile.Default;
        profile.Validate();
        var payload = CreatePayload(request, profile, stream: true);
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        using var response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;

            var data = line[5..].TrimStart();
            if (data.Length == 0)
                continue;
            if (data.Equals("[DONE]", StringComparison.Ordinal))
                yield break;

            StreamCompletionResponse chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<StreamCompletionResponse>(data, JsonOptions)
                    ?? throw new JsonException("Streaming response was null.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("llama-server returned invalid streaming JSON.", exception);
            }

            var text = chunk.Choices.FirstOrDefault()?.Delta?.Content;
            if (!string.IsNullOrEmpty(text))
                yield return new TranslationChunk(text);
        }
    }

    private object CreatePayload(TranslationRequest request, TranslationProfile profile, bool stream) => new
    {
        messages = new[] { new { role = "user", content = promptBuilder.Build(request) } },
        temperature = profile.Temperature,
        top_p = profile.TopP,
        top_k = profile.TopK,
        repeat_penalty = profile.RepetitionPenalty,
        max_tokens = profile.MaxTokens,
        stream
    };

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new HttpRequestException(
            $"llama-server request failed with {(int)response.StatusCode} ({response.ReasonPhrase}): {body}",
            null,
            response.StatusCode);
    }

    private sealed record CompletionResponse(Choice[] Choices, Usage? Usage);
    private sealed record Choice(Message? Message);
    private sealed record Message(string? Content);
    private sealed record Usage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
    private sealed record StreamCompletionResponse(StreamChoice[] Choices);
    private sealed record StreamChoice(Delta? Delta);
    private sealed record Delta(string? Content);
}
