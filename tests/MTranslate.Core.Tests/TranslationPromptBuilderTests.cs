using MTranslate.Core;
using Xunit;

namespace MTranslate.Core.Tests;

public sealed class TranslationPromptBuilderTests
{
    private readonly TranslationPromptBuilder builder = new();

    [Fact]
    public void Build_WithAutomaticSource_UsesShortPrompt()
    {
        var prompt = builder.Build(new TranslationRequest("Hello", "Chinese"));

        Assert.Equal(
            "Translate the following segment into Chinese, without additional explanation:\n\nHello",
            prompt);
    }

    [Fact]
    public void Build_WithExplicitSource_IncludesSourceLanguage()
    {
        var prompt = builder.Build(new TranslationRequest("Hello", "Chinese", "English"));

        Assert.Equal(
            "Translate the following English segment into Chinese, without additional explanation:\n\nHello",
            prompt);
    }

    [Fact]
    public void Build_WithContext_MarksOnlyCurrentSegmentForTranslation()
    {
        var prompt = builder.Build(new TranslationRequest("Bank", "Chinese", Context: "The river overflowed."));

        Assert.Contains("Do not translate the context.", prompt, StringComparison.Ordinal);
        Assert.Contains("CONTEXT:\nThe river overflowed.", prompt, StringComparison.Ordinal);
        Assert.EndsWith("CURRENT:\nBank", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_WithBlankText_Throws(string text)
    {
        Assert.Throws<ArgumentException>(() => builder.Build(new TranslationRequest(text, "Chinese")));
    }
}
