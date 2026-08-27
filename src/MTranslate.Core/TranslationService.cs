namespace MTranslate.Core;

public sealed record TranslationServiceRequest(
    string Text,
    string TargetLanguage,
    string? SourceLanguage = null,
    string ModelProfile = "default",
    string GlossaryVersion = "none",
    TranslationProfile? Profile = null,
    TranslationJobSource Source = TranslationJobSource.DesktopText,
    TranslationJobPriority Priority = TranslationJobPriority.Normal,
    ChunkingOptions? Chunking = null);

public sealed record TranslationServiceResult(
    string Text,
    int ChunkCount,
    int CacheHits,
    TimeSpan TotalDuration,
    int? PromptTokens,
    int? CompletionTokens);

public sealed class TranslationService(
    ITranslationClient client,
    IChunkManager chunkManager,
    ITranslationCache cache,
    ITranslationJobQueue jobQueue)
{
    public Task<TranslationServiceResult> TranslateAsync(
        TranslationServiceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        return jobQueue.EnqueueAsync(
            request.Source,
            request.Priority,
            token => TranslateCoreAsync(request, token),
            cancellationToken);
    }

    private async Task<TranslationServiceResult> TranslateCoreAsync(
        TranslationServiceRequest request,
        CancellationToken cancellationToken)
    {
        var chunks = chunkManager.Split(request.Text, request.Chunking);
        var translated = new System.Text.StringBuilder();
        var cacheHits = 0;
        var duration = TimeSpan.Zero;
        var promptTokens = 0;
        var completionTokens = 0;
        var hasPromptTokens = true;
        var hasCompletionTokens = true;
        string? previousSource = null;

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = new TranslationCacheKey(chunk.Text, request.SourceLanguage, request.TargetLanguage,
                request.ModelProfile, request.GlossaryVersion);
            var text = await cache.TryGetAsync(key, cancellationToken).ConfigureAwait(false);
            if (text is not null)
            {
                cacheHits++;
            }
            else
            {
                var result = await client.TranslateAsync(new TranslationRequest(
                    chunk.Text,
                    request.TargetLanguage,
                    request.SourceLanguage,
                    previousSource,
                    request.Profile), cancellationToken).ConfigureAwait(false);
                text = result.Text.Trim();
                duration += result.TotalDuration;
                if (result.PromptTokens is { } currentPrompt) promptTokens += currentPrompt; else hasPromptTokens = false;
                if (result.CompletionTokens is { } currentCompletion) completionTokens += currentCompletion; else hasCompletionTokens = false;
                await cache.SetAsync(key, text, cancellationToken).ConfigureAwait(false);
            }

            translated.Append(text);
            translated.Append(chunk.SeparatorAfter);
            previousSource = chunk.Text;
        }

        return new TranslationServiceResult(
            translated.ToString(), chunks.Count, cacheHits, duration,
            hasPromptTokens ? promptTokens : null,
            hasCompletionTokens ? completionTokens : null);
    }

    private static void Validate(TranslationServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("Translation text must not be empty.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TargetLanguage))
            throw new ArgumentException("Target language must not be empty.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ModelProfile))
            throw new ArgumentException("Model profile must not be empty.", nameof(request));
        request.Profile?.Validate();
    }
}
