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
}
