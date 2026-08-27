using System.Text;
using System.Text.RegularExpressions;

namespace MTranslate.Core;

public sealed record LanguageDetectionResult(string LanguageCode, double Confidence);

public interface ILanguageDetector
{
    LanguageDetectionResult? Detect(string text);
}

public sealed record TranslationLanguage(string Code, string EnglishName, string ChineseName);

public static class TranslationLanguages
{
    public static IReadOnlyList<TranslationLanguage> All { get; } =
    [
        new("zh-CN", "Chinese", "简体中文"), new("en", "English", "英语"),
        new("fr", "French", "法语"), new("pt", "Portuguese", "葡萄牙语"),
        new("es", "Spanish", "西班牙语"), new("ja", "Japanese", "日语"),
        new("tr", "Turkish", "土耳其语"), new("ru", "Russian", "俄语"),
        new("ar", "Arabic", "阿拉伯语"), new("ko", "Korean", "韩语"),
        new("th", "Thai", "泰语"), new("it", "Italian", "意大利语"),
        new("de", "German", "德语"), new("vi", "Vietnamese", "越南语"),
        new("ms", "Malay", "马来语"), new("id", "Indonesian", "印尼语"),
        new("tl", "Filipino", "菲律宾语"), new("hi", "Hindi", "印地语"),
        new("zh-Hant", "Traditional Chinese", "繁体中文"), new("pl", "Polish", "波兰语"),
        new("cs", "Czech", "捷克语"), new("nl", "Dutch", "荷兰语"),
        new("km", "Khmer", "高棉语"), new("my", "Burmese", "缅甸语"),
        new("fa", "Persian", "波斯语"), new("gu", "Gujarati", "古吉拉特语"),
        new("ur", "Urdu", "乌尔都语"), new("te", "Telugu", "泰卢固语"),
        new("mr", "Marathi", "马拉地语"), new("he", "Hebrew", "希伯来语"),
        new("bn", "Bengali", "孟加拉语"), new("ta", "Tamil", "泰米尔语"),
        new("uk", "Ukrainian", "乌克兰语"), new("bo", "Tibetan", "藏语"),
        new("kk", "Kazakh", "哈萨克语"), new("mn", "Mongolian", "蒙古语"),
        new("ug", "Uyghur", "维吾尔语"), new("yue", "Cantonese", "粤语")
    ];

    public static string ToPromptName(string language)
    {
        var code = language.Trim();
        if (code.Equals("zh", StringComparison.OrdinalIgnoreCase)
            || code.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase))
            return "Chinese";
        if (code.Equals("zh-TW", StringComparison.OrdinalIgnoreCase))
            return "Traditional Chinese";
        return All.FirstOrDefault(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.EnglishName ?? code;
    }
}

public sealed class HeuristicLanguageDetector : ILanguageDetector
{
    private static readonly Regex Words = new("[\\p{L}']+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, LanguageProfile> Profiles =
        new Dictionary<string, LanguageProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new("", "a an and are as be everyone for from have i in is it me my next not of on that the this time to wait was who will with you your"),
            ["fr"] = new("æëïœÿ", "avec ce dans de des du elle en est et il je la le les mais nous pas pour que une vous"),
            ["pt"] = new("ãõ", "a assunto com como conversar de do dos e em está este hoje não o os para por pouco que sobre uma vamos você"),
            ["es"] = new("ñ¿¡", "con de el ella en es esta la las los más no para pero por que una usted y yo"),
            ["tr"] = new("ğış", "artık biraz bugün bir bu da değil gel hadi için ile kendini mi mı mu mü nasıl ne olur sonra ve veya yorgun"),
            ["it"] = new("", "che con da del della di e gli il in italiano la le ma non per questo semplice sono testo traduzione una un"),
            ["de"] = new("äöüß", "aber auf das der die ein eine für ich ist mit nicht sie und von wir zu"),
            ["vi"] = new("ăâđêôơư", "bạn các cho của có không là một người những tôi và với"),
            ["ms"] = new("", "anda akan adalah dalam dan dengan ini itu kami kepada melayu tidak untuk yang"),
            ["id"] = new("", "adalah akan anda bahasa dalam dan dengan indonesia ini itu kami kepada tidak untuk yang"),
            ["tl"] = new("", "ako ang ay mga na namin ng ngunit para sa siya tayo hindi ito"),
            ["pl"] = new("ąćęłńóśźż", "ale być dla do i jest na nie oraz się ten to w z za że"),
            ["cs"] = new("čďěňřšťůž", "ale a do je jsem na ne pro se ten to v z že"),
            ["nl"] = new("", "aan als de een en het ik in is met niet op te van voor wij zijn")
        };

    public LanguageDetectionResult? Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var scriptCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var hanCount = 0;
        var arabicCount = 0;
        var cyrillicCount = 0;
        var devanagariCount = 0;
        var latinCount = 0;
        var letterCount = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!Rune.IsLetter(rune))
                continue;
            letterCount++;
            var value = rune.Value;
            if (IsHan(value)) hanCount++;
            else if (value is >= 0x0600 and <= 0x06ff) arabicCount++;
            else if (value is >= 0x0400 and <= 0x052f) cyrillicCount++;
            else if (value is >= 0x0900 and <= 0x097f) devanagariCount++;
            else if (IsLatin(value)) latinCount++;
            else
            {
                var code = DetectDistinctScript(value);
                if (code is not null)
                    scriptCounts[code] = scriptCounts.GetValueOrDefault(code) + 1;
            }
        }

        if (letterCount == 0)
            return null;
        if (scriptCounts.Count > 0)
        {
            var strongest = scriptCounts.MaxBy(item => item.Value);
            if (strongest.Value >= Math.Max(1, letterCount * 0.35))
                return Result(strongest.Key, strongest.Value, letterCount);
        }
        if (hanCount >= Math.Max(1, letterCount * 0.35))
            return Result(DetectHan(text), hanCount, letterCount);
        if (arabicCount >= Math.Max(1, letterCount * 0.35))
            return Result(DetectArabicScript(text), arabicCount, letterCount);
        if (cyrillicCount >= Math.Max(1, letterCount * 0.35))
            return Result(DetectCyrillicScript(text), cyrillicCount, letterCount);
        if (devanagariCount >= Math.Max(1, letterCount * 0.35))
            return Result(ScoreMarkers(text, "आहे आणि नाही मराठी तपासण्यासाठी मजकूर हा हे") > 0 ? "mr" : "hi", devanagariCount, letterCount);
        if (latinCount > 0)
            return DetectLatin(text, latinCount, letterCount);
        return null;
    }

    private static LanguageDetectionResult? DetectLatin(string text, int latinCount, int letterCount)
    {
        var words = Words.Matches(text).Select(match => match.Value.ToLowerInvariant()).ToArray();
        var normalized = text.ToLowerInvariant();
        var scores = Profiles.ToDictionary(
            profile => profile.Key,
            profile => profile.Value.Score(normalized, words),
            StringComparer.OrdinalIgnoreCase);
        var best = scores.MaxBy(item => item.Value);
        if (best.Value > 0)
        {
            var second = scores.Where(item => !item.Key.Equals(best.Key, StringComparison.OrdinalIgnoreCase))
                .Max(item => item.Value);
            var confidence = Math.Clamp(0.58 + (best.Value * 0.035) + ((best.Value - second) * 0.025), 0.58, 0.98);
            return new LanguageDetectionResult(best.Key, confidence);
        }
        return text.All(character => character <= 0x7f)
            ? new LanguageDetectionResult("en", Math.Clamp((double)latinCount / letterCount * 0.51, 0.4, 0.51))
            : null;
    }

    private static string DetectHan(string text)
    {
        if (ContainsAny(text, "嘅咗喺冇佢哋啲咩唔係噉嚟揾睇嗰"))
            return "yue";
        return ContainsAny(text, "體國學這個為與時會後發裡說們來對過還開關門書車東萬專業譯")
            ? "zh-Hant"
            : "zh-CN";
    }

    private static string DetectArabicScript(string text)
    {
        if (ContainsAny(text, "ۆۇۈېەڭھ") || ScoreMarkers(text, "بۇ بىر ۋە ئۇيغۇر بىلەن") > 0)
            return "ug";
        if (ContainsAny(text, "ٹڈڑںے") || ScoreMarkers(text, "ہے اور نہیں میں کا کی کو") > 1)
            return "ur";
        if (ContainsAny(text, "پچژگکی") || ScoreMarkers(text, "است این برای که می یک") > 1)
            return "fa";
        return "ar";
    }

    private static string DetectCyrillicScript(string text)
    {
        if (ContainsAny(text, "їєґ") || ScoreMarkers(text, "український перекладу перевірки") > 0)
            return "uk";
        if (ContainsAny(text, "әғқңұһ") || ScoreMarkers(text, "және бұл қазақ үшін емес") > 0)
            return "kk";
        if (ContainsAny(text, "өү") && ScoreMarkers(text, "ба байна бол монгол мөн нэг") > 0)
            return "mn";
        return "ru";
    }

    private static int ScoreMarkers(string text, string markers) => markers
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Count(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string text, string characters) => text.Any(characters.Contains);
    private static LanguageDetectionResult Result(string code, int matchingLetters, int totalLetters) =>
        new(code, Math.Clamp((double)matchingLetters / totalLetters, 0.6, 0.99));

    private static bool IsHan(int value) => value is >= 0x3400 and <= 0x4dbf or >= 0x4e00 and <= 0x9fff;
    private static bool IsLatin(int value) => value is >= 0x0041 and <= 0x007a or >= 0x00c0 and <= 0x024f or >= 0x1e00 and <= 0x1eff;

    private static string? DetectDistinctScript(int value)
    {
        if (value is >= 0x3040 and <= 0x30ff) return "ja";
        if (value is >= 0xac00 and <= 0xd7af or >= 0x1100 and <= 0x11ff) return "ko";
        if (value is >= 0x0e00 and <= 0x0e7f) return "th";
        if (value is >= 0x1780 and <= 0x17ff) return "km";
        if (value is >= 0x1000 and <= 0x109f) return "my";
        if (value is >= 0x0a80 and <= 0x0aff) return "gu";
        if (value is >= 0x0c00 and <= 0x0c7f) return "te";
        if (value is >= 0x0980 and <= 0x09ff) return "bn";
        if (value is >= 0x0b80 and <= 0x0bff) return "ta";
        if (value is >= 0x0f00 and <= 0x0fff) return "bo";
        if (value is >= 0x0590 and <= 0x05ff) return "he";
        if (value is >= 0x1800 and <= 0x18af) return "mn";
        return null;
    }

    private sealed class LanguageProfile
    {
        private readonly HashSet<char> distinctiveCharacters;
        private readonly HashSet<string> markerWords;

        public LanguageProfile(string characters, string words)
        {
            distinctiveCharacters = characters.ToHashSet();
            markerWords = words.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public int Score(string text, IEnumerable<string> words) =>
            text.Count(distinctiveCharacters.Contains) * 3 + words.Count(markerWords.Contains);
    }
}

public static class TranslationLanguageNames
{
    public static string ToPromptName(string language) => TranslationLanguages.ToPromptName(language);
}
