namespace MTranslate.DocumentFormats.Tests;

public sealed class VttDocumentParserTests
{
    private readonly VttDocumentParser parser = new();
    private const string Input = "WEBVTT - Demo\n\nSTYLE\n::cue { color: lime; }\n\nNOTE This note stays\nNever translate this.\n\nintro\n00:01.000 --> 00:03.500 line:90%\nHello world.\n\n00:04.000 --> 00:06.000\nSecond cue.\n";

    [Fact]
    public async Task Parser_PreservesHeaderStyleNoteIdentifierAndSettings()
    {
        var document = await ParserTestSupport.ParseAsync(parser, Input);

        Assert.Equal(2, document.SubtitleCues?.Count);
        Assert.Equal("intro", document.SubtitleCues![0].Identifier);
        Assert.Equal("line:90%", document.SubtitleCues[0].Settings);
        Assert.DoesNotContain(document.TranslatableParts, part => part.Content.Contains("Never translate"));
    }

    [Fact]
    public async Task RoundTrip_PreservesCompleteVtt()
    {
        var document = await ParserTestSupport.ParseAsync(parser, Input);

        Assert.Equal(Input, await ParserTestSupport.WriteTextAsync(parser, document));
    }

    [Fact]
    public async Task Translation_ChangesOnlyCueTextAndPassesValidation()
    {
        var document = await ParserTestSupport.ParseAsync(parser, Input);
        var translations = ParserTestSupport.TranslateAll(document, part => part.Id.EndsWith("0") ? "你好，世界。" : "第二条字幕。");
        var bytes = await ParserTestSupport.WriteBytesAsync(parser, document, translations);
        await using var validation = new MemoryStream(bytes);

        await parser.ValidateAsync(document, validation);
        var output = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("NOTE This note stays\nNever translate this.", output);
        Assert.Contains("00:01.000 --> 00:03.500 line:90%\n你好，世界。", output);
    }
}
