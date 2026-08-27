namespace MTranslate.Desktop.Services;

using MTranslate.DocumentFormats;

public sealed record DesktopTranslationResponse(string Text, TimeSpan Elapsed, int CacheHits, int ChunkCount);

public interface ITranslationCoordinator
{
    bool CacheEnabled { get; set; }
    string ModelStatus { get; }
    Task<DesktopTranslationResponse> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default);
    Task<DocumentTranslationResult> TranslateDocumentAsync(
        string inputPath,
        string outputPath,
        string sourceLanguage,
        string targetLanguage,
        SubtitleOutputMode subtitleOutput,
        Guid jobId,
        IProgress<DocumentTranslationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IFilePickerService
{
    Task<IReadOnlyList<string>> PickDocumentsAsync();
    Task<string?> PickOutputFolderAsync();
    Task OpenContainingFolderAsync(string path);
}

public interface IClipboardService
{
    Task SetTextAsync(string text);
}
