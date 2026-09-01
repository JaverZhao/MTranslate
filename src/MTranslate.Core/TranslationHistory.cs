namespace MTranslate.Core;

public sealed record TranslationHistoryEntry(
    Guid Id,
    string SourceText,
    string TranslatedText,
    string SourceLanguage,
    string TargetLanguage,
    string ModelId,
    DateTimeOffset CreatedAt,
    TimeSpan Elapsed);

public interface ITranslationHistoryStore
{
    Task AddAsync(TranslationHistoryEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TranslationHistoryEntry>> SearchAsync(
        string? query = null,
        int limit = 200,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
