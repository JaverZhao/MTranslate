namespace MTranslate.DocumentFormats.Tests;

public sealed class SrtDocumentParserTests
{
    private readonly SrtDocumentParser parser = new();
    private const string Input = "1\r\n00:00:01,000 --> 00:00:03,500\r\nHello, everyone.\r\n\r\n2\r\n00:00:04,000 --> 00:00:06,000\r\nSecond line.\r\nContinued.\r\n";

    [Fact]
    public async Task Parser_ExtractsCueIndexTimingAndMultilineText()
    {
        var document = await ParserTestSupport.ParseAsync(parser, Input);

        Assert.Equal(2, document.SubtitleCues?.Count);
        Assert.Equal("1", document.SubtitleCues![0].Identifier);
        Assert.Equal(TimeSpan.FromSeconds(1), document.SubtitleCues[0].Start);
        Assert.Equal(TimeSpan.FromMilliseconds(3500), document.SubtitleCues[0].End);
        Assert.Equal("Second line.\r\nContinued.", document.TranslatableParts[1].Content);
    }

    [Fact]
    public async Task RoundTrip_PreservesIndexesTimestampsAndText()
    {
        var document = await ParserTestSupport.ParseAsync(parser, Input);

        Assert.Equal(Input, await ParserTestSupport.WriteTextAsync(parser, document));
    }

    [Fact]
    public async Task Translation_SupportsBilingualOutputWithoutChangingTiming()
    {
        var document = await ParserTestSupport.ParseAsync(parser, Input);
        var translations = ParserTestSupport.TranslateAll(document, part => part.Id.EndsWith("0") ? "大家好。" : "第二行。\r\n继续。" );
        var bytes = await ParserTestSupport.WriteBytesAsync(
            parser,
            document,
            translations,
            new DocumentWriteOptions(SubtitleOutputMode.OriginalThenTranslation));
        await using var validation = new MemoryStream(bytes);

        await parser.ValidateAsync(document, validation);
        var output = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("Hello, everyone.\r\n大家好。", output);
        Assert.Contains("00:00:04,000 --> 00:00:06,000", output);
    }
}
