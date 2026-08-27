namespace MTranslate.DocumentFormats.Tests;

public sealed class MarkdownDocumentParserTests
{
    private readonly MarkdownDocumentParser parser = new();
    private const string Input = """
        ---
        title: Local translation
        slug: local-translation
        ---
        # Welcome

        Read [OpenAI documentation](https://openai.com/docs) and `dotnet test`.

        <div class="notice">Local only</div>

        ```csharp
        Console.WriteLine("Do not translate");
        ```
        """;

    [Fact]
    public async Task Parser_ProtectsCodeUrlsHtmlTagsAndFrontMatterKeys()
    {
        var document = await ParserTestSupport.ParseAsync(parser, Input);
        var translatedText = string.Join('|', document.TranslatableParts.Select(part => part.Content));

        Assert.Contains("Local translation", translatedText);
        Assert.Contains("OpenAI documentation", translatedText);
        Assert.Contains("Local only", translatedText);
        Assert.DoesNotContain("https://openai.com/docs", translatedText);
        Assert.DoesNotContain("dotnet test", translatedText);
        Assert.DoesNotContain("Console.WriteLine", translatedText);
        Assert.DoesNotContain("title:", translatedText);
    }

    [Fact]
    public async Task RoundTrip_PreservesCompleteMarkdown()
    {
        var document = await ParserTestSupport.ParseAsync(parser, Input);

        Assert.Equal(Input, await ParserTestSupport.WriteTextAsync(parser, document));
    }

    [Fact]
    public async Task Translation_PreservesProtectedMarkdownStructureAndValidates()
    {
        var document = await ParserTestSupport.ParseAsync(parser, Input);
        var translations = ParserTestSupport.TranslateAll(document, part => $"译文{part.Id[^2..]}");
        var bytes = await ParserTestSupport.WriteBytesAsync(parser, document, translations);
        await using var validation = new MemoryStream(bytes);

        await parser.ValidateAsync(document, validation);
        var output = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("[译文", output);
        Assert.Contains("](https://openai.com/docs)", output);
        Assert.Contains("`dotnet test`", output);
        Assert.Contains("Console.WriteLine(\"Do not translate\");", output);
        Assert.Contains("title:", output);
    }
}
