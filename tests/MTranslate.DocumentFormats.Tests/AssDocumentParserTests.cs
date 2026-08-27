namespace MTranslate.DocumentFormats.Tests;

public sealed class AssDocumentParserTests
{
    private readonly AssDocumentParser parser = new();
    private const string Input = """
        [Script Info]
        Title: Demo

        [V4+ Styles]
        Format: Name, Fontname, Fontsize
        Style: Default,Arial,20

        [Events]
        Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
        Dialogue: 0,0:00:01.00,0:00:03.00,Default,,0,0,0,,{\an8}Hello\Nworld
        Comment: 0,0:00:04.00,0:00:05.00,Default,,0,0,0,,Do not translate
        """;

    [Fact]
    public async Task Parser_ExtractsDialogueTextAndProtectsTagsAndComments()
    {
        var document = await ParserTestSupport.ParseAsync(parser, Input);

        Assert.Equal(["Hello", "world"], document.TranslatableParts.Select(part => part.Content));
        Assert.DoesNotContain(document.TranslatableParts, part => part.Content.Contains("Do not translate"));
    }

    [Fact]
    public async Task RoundTrip_PreservesCompleteAssDocument()
    {
        var document = await ParserTestSupport.ParseAsync(parser, Input);

        Assert.Equal(Input, await ParserTestSupport.WriteTextAsync(parser, document));
    }

    [Fact]
    public async Task Translation_PreservesOverrideTagsNewlineAndEventFields()
    {
        var document = await ParserTestSupport.ParseAsync(parser, Input);
        var translations = new Dictionary<string, string>
        {
            [document.TranslatableParts[0].Id] = "你好",
            [document.TranslatableParts[1].Id] = "世界"
        };
        var bytes = await ParserTestSupport.WriteBytesAsync(parser, document, translations);
        await using var validation = new MemoryStream(bytes);

        await parser.ValidateAsync(document, validation);
        var output = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("{\\an8}你好\\N世界", output);
        Assert.Contains("Comment: 0,0:00:04.00,0:00:05.00,Default,,0,0,0,,Do not translate", output);
    }
}
