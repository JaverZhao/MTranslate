using System.Text;

namespace MTranslate.DocumentFormats.Tests;

public sealed class TxtDocumentParserTests
{
    private readonly TxtDocumentParser parser = new();
    private const string Input = "First paragraph.\r\n  Still first.  \r\n\r\nSecond paragraph.\r\n";

    [Fact]
    public async Task Parser_SeparatesLogicalLinesAndDetectsBom()
    {
        var document = await ParserTestSupport.ParseAsync(parser, Input, bom: true);

        Assert.Equal(DocumentFormat.Txt, document.Format);
        Assert.True(document.HasByteOrderMark);
        Assert.Equal(3, document.TranslatableParts.Count);
        Assert.Equal("First paragraph.", document.TranslatableParts[0].Content);
        Assert.Equal("Still first.", document.TranslatableParts[1].Content);
        Assert.Equal("Second paragraph.", document.TranslatableParts[2].Content);
    }

    [Fact]
    public async Task RoundTrip_PreservesBytesIncludingBomAndLineEndings()
    {
        var document = await ParserTestSupport.ParseAsync(parser, Input, bom: true);
        var output = await ParserTestSupport.WriteBytesAsync(parser, document);
        var expected = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Input)).ToArray();

        Assert.Equal(expected, output);
    }

    [Fact]
    public async Task Translation_ReplacesOnlyParagraphText()
    {
        var document = await ParserTestSupport.ParseAsync(parser, Input);
        var translations = new Dictionary<string, string>
        {
            [document.TranslatableParts[0].Id] = "第一行。",
            [document.TranslatableParts[1].Id] = "仍然是第一段。",
            [document.TranslatableParts[2].Id] = "第二段。"
        };

        var output = await ParserTestSupport.WriteTextAsync(parser, document, translations);

        Assert.Equal("第一行。\r\n  仍然是第一段。  \r\n\r\n第二段。\r\n", output);
    }

    [Fact]
    public async Task Translation_PreservesLfBlankLinesAndTrailingNewline()
    {
        const string input = "Line one.\nLine two.\n\nLine four.\n";
        var document = await ParserTestSupport.ParseAsync(parser, input);
        var translations = ParserTestSupport.TranslateAll(document, part => $"译{part.Id[^1]}");

        var output = await ParserTestSupport.WriteTextAsync(parser, document, translations);

        Assert.Equal("译0\n译1\n\n译2\n", output);
    }
}
