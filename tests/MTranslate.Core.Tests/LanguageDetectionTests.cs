using MTranslate.Core;

namespace MTranslate.Core.Tests;

public sealed class LanguageDetectionTests
{
    private readonly HeuristicLanguageDetector detector = new();

    [Theory]
    [InlineData("Everyone who called me fat—just wait! Next time you see me, I’ll be skinny as a rail!", "en")]
    [InlineData("这是一个完全本地运行的翻译工具。", "zh-CN")]
    [InlineData("これはローカルで動作する翻訳ツールです。", "ja")]
    [InlineData("이 번역 도구는 로컬에서 실행됩니다.", "ko")]
    [InlineData("Ceci est un outil de traduction qui fonctionne en local.", "fr")]
    [InlineData("Esta es una herramienta de traducción para usted.", "es")]
    [InlineData("Das ist ein Übersetzungswerkzeug für die lokale Nutzung.", "de")]
    [InlineData("Hoje vamos conversar um pouco sobre este assunto.", "pt")]
    [InlineData("Bugün biraz yorgun musun? Hadi tanışalım ve sohbet edelim.", "tr")]
    [InlineData("Это простой русский текст для проверки перевода.", "ru")]
    [InlineData("هذا نص عربي بسيط لاختبار الترجمة.", "ar")]
    [InlineData("Questo è un semplice testo italiano per la traduzione.", "it")]
    [InlineData("Đây là một văn bản tiếng Việt để kiểm tra bản dịch.", "vi")]
    [InlineData("Ini adalah teks Melayu untuk anda dan kami.", "ms")]
    [InlineData("Ini adalah teks bahasa Indonesia untuk Anda.", "id")]
    [InlineData("Ito ay isang tekstong Filipino para sa mga kaibigan.", "tl")]
    [InlineData("यह अनुवाद की जाँच के लिए एक हिंदी पाठ है।", "hi")]
    [InlineData("這是一個用來測試翻譯功能的繁體中文句子。", "zh-Hant")]
    [InlineData("To jest polski tekst do sprawdzenia tłumaczenia.", "pl")]
    [InlineData("Toto je český text pro kontrolu překladu.", "cs")]
    [InlineData("Dit is een Nederlandse tekst voor de vertaling.", "nl")]
    [InlineData("នេះជាអត្ថបទខ្មែរសម្រាប់សាកល្បងការបកប្រែ។", "km")]
    [InlineData("ဤသည်မှာ ဘာသာပြန်စမ်းသပ်ရန် မြန်မာစာဖြစ်သည်။", "my")]
    [InlineData("این یک متن فارسی برای آزمایش ترجمه است.", "fa")]
    [InlineData("આ અનુવાદ ચકાસવા માટેનું ગુજરાતી લખાણ છે.", "gu")]
    [InlineData("یہ ترجمہ جانچنے کے لیے ایک اردو متن ہے۔", "ur")]
    [InlineData("ఇది అనువాదాన్ని పరీక్షించడానికి తెలుగు వాక్యం.", "te")]
    [InlineData("हा अनुवाद तपासण्यासाठी एक मराठी मजकूर आहे.", "mr")]
    [InlineData("זהו טקסט בעברית לבדיקת התרגום.", "he")]
    [InlineData("এটি অনুবাদ পরীক্ষা করার জন্য একটি বাংলা লেখা।", "bn")]
    [InlineData("இது மொழிபெயர்ப்பைச் சோதிக்க ஒரு தமிழ் உரை.", "ta")]
    [InlineData("Це український текст для перевірки перекладу.", "uk")]
    [InlineData("འདི་ནི་སྐད་སྒྱུར་ཚོད་ལྟའི་བོད་ཡིག་ཡིན།", "bo")]
    [InlineData("Бұл қазақ тіліндегі аударманы тексеру мәтіні.", "kk")]
    [InlineData("Энэ бол монгол хэлний орчуулгыг шалгах нэг өгүүлбэр мөн.", "mn")]
    [InlineData("بۇ ئۇيغۇرچە تەرجىمىنى سىناش ئۈچۈن بىر تېكىست.", "ug")]
    [InlineData("呢個係用嚟測試翻譯嘅廣東話句子。", "yue")]
    public void Detect_RecognizesSupportedLanguage(string text, string expectedCode)
    {
        Assert.Equal(expectedCode, detector.Detect(text)?.LanguageCode);
    }

    [Fact]
    public void Detect_ReturnsNullForContentWithoutLetters()
    {
        Assert.Null(detector.Detect("1234 -- 5678"));
    }

    [Theory]
    [InlineData("zh-CN", "Chinese")]
    [InlineData("zh-TW", "Traditional Chinese")]
    [InlineData("en", "English")]
    [InlineData("ja", "Japanese")]
    public void PromptName_MapsBcp47Codes(string code, string expectedName)
    {
        Assert.Equal(expectedName, TranslationLanguageNames.ToPromptName(code));
    }

    [Fact]
    public void PromptBuilder_UsesModelLanguageNamesForBcp47Codes()
    {
        var prompt = new TranslationPromptBuilder().Build(new TranslationRequest("Hello", "zh-CN", "en"));

        Assert.Equal(
            "Translate the following English segment into Chinese, without additional explanation:\n\nHello",
            prompt);
    }

    [Fact]
    public void Catalog_ContainsEveryOfficialHyMt2Language()
    {
        Assert.Equal(38, TranslationLanguages.All.Count);
        Assert.Equal(38, TranslationLanguages.All.Select(language => language.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("Turkish", TranslationLanguages.ToPromptName("tr"));
        Assert.Equal("Traditional Chinese", TranslationLanguages.ToPromptName("zh-TW"));
    }
}
