using MTranslate.Core;

namespace MTranslate.Core.Tests;

public sealed class ChunkManagerTests
{
    [Fact]
    public void Split_PrefersParagraphBoundariesAndPreservesWhitespace()
    {
        var manager = new ChunkManager(new CharacterTokenEstimator());
        const string input = "First paragraph.\n\nSecond paragraph is longer.\n\nThird.";

        var chunks = manager.Split(input, new ChunkingOptions(15, 30));

        Assert.True(chunks.Count >= 2);
        Assert.Contains("\n\n", chunks[0].SeparatorAfter);
        Assert.Equal(input, string.Concat(chunks.Select(chunk => chunk.Text + chunk.SeparatorAfter)));
    }

    [Fact]
    public void Split_DoesNotCutLatinWordWhenWhitespaceIsAvailable()
    {
        var manager = new ChunkManager(new CharacterTokenEstimator());
        const string input = "alpha bravo charlie delta echo foxtrot";

        var chunks = manager.Split(input, new ChunkingOptions(10, 14));

        Assert.All(chunks.Take(chunks.Count - 1), chunk => Assert.EndsWith(" ", chunk.SeparatorAfter));
        Assert.Equal(input, string.Concat(chunks.Select(chunk => chunk.Text + chunk.SeparatorAfter)));
    }

    [Fact]
    public void Split_NormalizesLineEndingsAndUnicode()
    {
        var manager = new ChunkManager();

        var chunks = manager.Split("Cafe\u0301\r\n下一行");

        Assert.Single(chunks);
        Assert.Equal("Café\n下一行", chunks[0].Text);
    }

    [Fact]
    public void Split_DoesNotBreakAnOversizedWord()
    {
        var manager = new ChunkManager(new CharacterTokenEstimator());
        const string input = "extraordinarilylongword short";

        var chunks = manager.Split(input, new ChunkingOptions(5, 10));

        Assert.Equal("extraordinarilylongword", chunks[0].Text);
        Assert.Equal(" ", chunks[0].SeparatorAfter);
    }

    private sealed class CharacterTokenEstimator : ITokenEstimator
    {
        public int Estimate(string text) => text.Length;
    }
}
