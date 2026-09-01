namespace MTranslate.Desktop.Services;

using MTranslate.Core;
using MTranslate.DocumentFormats;

public sealed record DesktopTranslationResponse(string Text, TimeSpan Elapsed, int CacheHits, int ChunkCount);

public enum InferenceAccelerationMode
{
    Automatic,
    Cpu,
    Gpu
}

public sealed record DesktopModelInfo(
    string Id,
    string DisplayName,
    string Quantization,
    long SizeBytes,
    string Status,
    bool IsInstalled,
    bool IsActive,
    bool RuntimeAvailable)
{
    public bool CanDownload => !IsInstalled;
    public bool CanSelect => IsInstalled && RuntimeAvailable && !IsActive;
}

public interface ITranslationCoordinator
{
    bool CacheEnabled { get; set; }
    bool HistoryEnabled { get => true; set { } }
    InferenceAccelerationMode AccelerationMode { get => InferenceAccelerationMode.Automatic; set { } }
    string AccelerationStatus => "自动";
    string ModelStatus { get; }
    IReadOnlyList<DesktopModelInfo> ModelInfos => [];
    Task DownloadModelAsync(
        string modelId,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Model downloads are not supported by this coordinator."));
    Task SelectModelAsync(string modelId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task<IReadOnlyList<TranslationHistoryEntry>> SearchHistoryAsync(
        string? query = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TranslationHistoryEntry>>([]);
    Task<bool> DeleteHistoryAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
    Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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
