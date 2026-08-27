using System.Runtime.CompilerServices;

namespace MTranslate.Core;

public sealed record TranslationRequest(
    string Text,
    string TargetLanguage,
    string? SourceLanguage = null,
    string? Context = null,
    TranslationProfile? Profile = null);

public sealed record TranslationResult(
    string Text,
    TimeSpan TimeToFirstToken,
    TimeSpan TotalDuration,
    int? PromptTokens,
    int? CompletionTokens);

public sealed record TranslationChunk(string Text);

public sealed record TranslationProfile(
    double Temperature,
    double TopP,
    int TopK,
    double RepetitionPenalty,
    int MaxTokens)
{
    public static TranslationProfile Default { get; } = new(0.7, 0.6, 20, 1.05, 4096);

    public void Validate()
    {
        if (Temperature is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(Temperature));
        if (TopP is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(TopP));
        if (TopK < 0)
            throw new ArgumentOutOfRangeException(nameof(TopK));
        if (RepetitionPenalty <= 0)
            throw new ArgumentOutOfRangeException(nameof(RepetitionPenalty));
        if (MaxTokens <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxTokens));
    }
}

public interface ITranslationClient
{
    Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<TranslationChunk> TranslateStreamingAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default);
}

public interface ITranslationPromptBuilder
{
    string Build(TranslationRequest request);
}

public interface IModelDownloader
{
    Task DownloadAsync(
        Uri source,
        string destinationPath,
        string expectedSha256,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record DownloadProgress(long BytesDownloaded, long? TotalBytes)
{
    public double? Percentage => TotalBytes is > 0
        ? BytesDownloaded * 100d / TotalBytes.Value
        : null;
}

public sealed class TranslationPromptBuilder : ITranslationPromptBuilder
{
    public string Build(TranslationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("Translation text must not be empty.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TargetLanguage))
            throw new ArgumentException("Target language must not be empty.", nameof(request));

        var target = TranslationLanguageNames.ToPromptName(request.TargetLanguage);
        var source = string.IsNullOrWhiteSpace(request.SourceLanguage)
            ? null
            : TranslationLanguageNames.ToPromptName(request.SourceLanguage);
        var context = request.Context?.Trim();

        if (!string.IsNullOrEmpty(context))
        {
            return $"Use the previous context only to understand meaning and terminology.\n" +
                   $"Translate only the segment marked CURRENT into {target}.\n" +
                   "Do not translate the context.\n\n" +
                   $"CONTEXT:\n{context}\n\nCURRENT:\n{request.Text}";
        }

        return string.IsNullOrEmpty(source)
            ? $"Translate the following segment into {target}, without additional explanation:\n\n{request.Text}"
            : $"Translate the following {source} segment into {target}, without additional explanation:\n\n{request.Text}";
    }
}
