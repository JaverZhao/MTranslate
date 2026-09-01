using System.Security.Cryptography;
using System.Text;

namespace MTranslate.Core;

public sealed record TranslationCacheKey(
    string SourceText,
    string? SourceLanguage,
    string TargetLanguage,
    string ModelProfile,
    string GlossaryVersion = "none",
    string? Context = null)
{
    public string ComputeHash()
    {
        var normalized = SourceText.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim()
            .Normalize(NormalizationForm.FormC);
        var canonical = string.Join('\u001f', normalized, SourceLanguage?.Trim().ToLowerInvariant() ?? "auto",
            TargetLanguage.Trim().ToLowerInvariant(), ModelProfile.Trim(), GlossaryVersion.Trim(), Context?.Trim() ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

public sealed record TranslationCacheEntry(
    string Hash,
    string SourceLanguage,
    string TargetLanguage,
    string ModelProfile,
    string SourceText,
    string TranslatedText,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    long HitCount);

public interface ITranslationCache
{
    bool Enabled { get; set; }
    Task<string?> TryGetAsync(TranslationCacheKey key, CancellationToken cancellationToken = default);
    Task SetAsync(TranslationCacheKey key, string translatedText, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    Task TrimAsync(long maximumBytes = 500L * 1024 * 1024, CancellationToken cancellationToken = default);
}

public sealed class NullTranslationCache : ITranslationCache
{
    public bool Enabled { get; set; }
    public Task<string?> TryGetAsync(TranslationCacheKey key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    public Task SetAsync(TranslationCacheKey key, string translatedText, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task TrimAsync(long maximumBytes = 500L * 1024 * 1024, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
