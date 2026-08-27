using System.Text;
using MTranslate.Core;

namespace MTranslate.DocumentFormats;

public enum DocumentFormat { Txt, Srt, Vtt, Markdown, Ass }
public enum DocumentPartKind { Structure, Text, SubtitleText, MetadataValue }
public enum SubtitleOutputMode { TranslationOnly, OriginalThenTranslation, TranslationThenOriginal }

public sealed record DocumentPart(
    string Id,
    string Content,
    bool IsTranslatable,
    DocumentPartKind Kind = DocumentPartKind.Structure);

public sealed record SubtitleCue(
    string Id,
    string? Identifier,
    TimeSpan Start,
    TimeSpan End,
    string Settings,
    string SegmentId);

public sealed record ParsedDocument(
    DocumentFormat Format,
    Encoding Encoding,
    bool HasByteOrderMark,
    IReadOnlyList<DocumentPart> Parts,
    IReadOnlyList<SubtitleCue>? SubtitleCues = null,
    IReadOnlyDictionary<string, string>? ProtectedMetadata = null)
{
    public IReadOnlyList<DocumentPart> TranslatableParts => Parts.Where(part => part.IsTranslatable).ToArray();
    public string OriginalText => string.Concat(Parts.Select(part => part.Content));
}

public sealed record DocumentWriteOptions(
    SubtitleOutputMode SubtitleOutput = SubtitleOutputMode.TranslationOnly);

public interface IDocumentParser
{
    DocumentFormat Format { get; }
    bool CanHandle(string extension);
    Task<ParsedDocument> ParseAsync(Stream input, CancellationToken cancellationToken = default);
    Task WriteAsync(
        ParsedDocument document,
        IReadOnlyDictionary<string, string> translations,
        Stream output,
        DocumentWriteOptions? options = null,
        CancellationToken cancellationToken = default);
    Task ValidateAsync(
        ParsedDocument source,
        Stream translatedOutput,
        CancellationToken cancellationToken = default);
}

public sealed record DocumentTranslationRequest(
    string InputPath,
    string OutputPath,
    string TargetLanguage,
    string? SourceLanguage = null,
    string ModelProfile = "standard-q4-k-m/prompt-v2-language-detection",
    string GlossaryVersion = "none",
    SubtitleOutputMode SubtitleOutput = SubtitleOutputMode.TranslationOnly,
    Guid? JobId = null);

public sealed record DocumentTranslationProgress(
    int CompletedSourceTokens,
    int TotalSourceTokens,
    int CompletedSegments,
    int TotalSegments)
{
    public double Percentage => TotalSourceTokens == 0 ? 100 : CompletedSourceTokens * 100d / TotalSourceTokens;
}

public sealed record DocumentTranslationResult(
    Guid JobId,
    string OutputPath,
    int SegmentCount,
    int SourceTokens,
    TimeSpan Duration,
    bool ResumedFromCheckpoint);

public interface IDocumentTranslator
{
    Task<DocumentTranslationResult> TranslateAsync(
        DocumentTranslationRequest request,
        IProgress<DocumentTranslationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record DocumentTranslationCheckpoint(
    Guid JobId,
    string FileHash,
    string TargetLanguage,
    string ModelProfile,
    string OutputTempFile,
    IReadOnlyDictionary<string, string> CompletedSegments,
    DateTimeOffset UpdatedAt);

public interface IDocumentCheckpointStore
{
    Task<DocumentTranslationCheckpoint?> LoadAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task SaveAsync(DocumentTranslationCheckpoint checkpoint, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid jobId, CancellationToken cancellationToken = default);
}
