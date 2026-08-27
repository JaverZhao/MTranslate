using System.Text;
using System.Text.RegularExpressions;

namespace MTranslate.Core;

public sealed record LanguageDetectionResult(string LanguageCode, double Confidence);

public interface ILanguageDetector
{
    LanguageDetectionResult? Detect(string text);
}

public sealed class HeuristicLanguageDetector : ILanguageDetector
{
    private static readonly Regex Words = new("[\\p{L}']+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly IReadOnlyDictionary<string, HashSet<string>> LatinMarkers =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new(["a", "an", "and", "are", "as", "be", "everyone", "for", "from", "i", "in", "is", "it", "me", "my", "next", "of", "on", "that", "the", "this", "time", "to", "wait", "who", "will", "with", "you", "your"]),
            ["de"] = new(["aber", "auf", "das", "der", "die", "ein", "eine", "für", "ich", "ist", "mit", "nicht", "sie", "und", "von", "wir", "zu"]),
            ["fr"] = new(["avec", "ce", "dans", "de", "des", "du", "elle", "en", "est", "et", "il", "je", "la", "le", "les", "mais", "nous", "pas", "pour", "une", "vous"]),
            ["es"] = new(["con", "de", "el", "ella", "en", "es", "esta", "la", "las", "los", "más", "no", "para", "pero", "por", "que", "una", "usted", "y", "yo"])
        };

    public LanguageDetectionResult? Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var letterCount = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!Rune.IsLetter(rune))
                continue;
            letterCount++;
            var code = DetectScript(rune.Value);
            if (code is not null)
                counts[code] = counts.GetValueOrDefault(code) + 1;
        }

        if (letterCount == 0)
            return null;
        if (counts.Count > 0)
        {
            var strongest = counts.MaxBy(item => item.Value);
            var confidence = (double)strongest.Value / letterCount;
            if (strongest.Key is "ja" or "ko" or "ar" or "ru" || confidence >= 0.45)
                return new LanguageDetectionResult(strongest.Key, Math.Clamp(confidence, 0.55, 0.99));
        }

        var words = Words.Matches(text).Select(match => match.Value.ToLowerInvariant()).ToArray();
        if (words.Length == 0)
            return null;
        var scores = LatinMarkers.ToDictionary(
            item => item.Key,
            item => words.Count(item.Value.Contains),
            StringComparer.OrdinalIgnoreCase);
        var best = scores.MaxBy(item => item.Value);
        if (best.Value > 0)
            return new LanguageDetectionResult(best.Key, Math.Min(0.98, 0.6 + (0.08 * best.Value)));

        return text.All(character => character <= 0x7f)
            ? new LanguageDetectionResult("en", 0.51)
            : null;
    }

    private static string? DetectScript(int value)
    {
        if (value is >= 0x3040 and <= 0x30ff)
            return "ja";
        if (value is >= 0xac00 and <= 0xd7af or >= 0x1100 and <= 0x11ff)
            return "ko";
        if (value is >= 0x4e00 and <= 0x9fff or >= 0x3400 and <= 0x4dbf)
            return "zh-CN";
        if (value is >= 0x0600 and <= 0x06ff)
            return "ar";
        if (value is >= 0x0400 and <= 0x04ff)
            return "ru";
        return null;
    }
}

public static class TranslationLanguageNames
{
    private static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["zh"] = "Chinese", ["zh-CN"] = "Chinese", ["zh-Hans"] = "Chinese",
            ["zh-TW"] = "Traditional Chinese", ["zh-Hant"] = "Traditional Chinese",
            ["en"] = "English", ["ja"] = "Japanese", ["ko"] = "Korean",
            ["de"] = "German", ["fr"] = "French", ["es"] = "Spanish",
            ["pt"] = "Portuguese", ["pt-BR"] = "Portuguese", ["tr"] = "Turkish",
            ["ar"] = "Arabic", ["vi"] = "Vietnamese", ["th"] = "Thai",
            ["id"] = "Indonesian", ["ru"] = "Russian", ["it"] = "Italian"
        };

    public static string ToPromptName(string language) => Names.TryGetValue(language.Trim(), out var name)
        ? name
        : language.Trim();
}
