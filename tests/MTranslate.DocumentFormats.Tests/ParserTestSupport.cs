using System.Text;

namespace MTranslate.DocumentFormats.Tests;

internal static class ParserTestSupport
{
    public static async Task<ParsedDocument> ParseAsync(IDocumentParser parser, string text, bool bom = false)
    {
        var content = Encoding.UTF8.GetBytes(text);
        var bytes = bom ? Encoding.UTF8.GetPreamble().Concat(content).ToArray() : content;
        await using var stream = new MemoryStream(bytes);
        return await parser.ParseAsync(stream);
    }

    public static async Task<byte[]> WriteBytesAsync(
        IDocumentParser parser,
        ParsedDocument document,
        IReadOnlyDictionary<string, string>? translations = null,
        DocumentWriteOptions? options = null)
    {
        await using var output = new MemoryStream();
        await parser.WriteAsync(document, translations ?? new Dictionary<string, string>(), output, options);
        return output.ToArray();
    }

    public static async Task<string> WriteTextAsync(
        IDocumentParser parser,
        ParsedDocument document,
        IReadOnlyDictionary<string, string>? translations = null,
        DocumentWriteOptions? options = null)
    {
        var bytes = await WriteBytesAsync(parser, document, translations, options);
        var preamble = document.HasByteOrderMark ? document.Encoding.GetPreamble().Length : 0;
        return document.Encoding.GetString(bytes, preamble, bytes.Length - preamble);
    }

    public static Dictionary<string, string> TranslateAll(ParsedDocument document, Func<DocumentPart, string> translate) =>
        document.TranslatableParts.ToDictionary(part => part.Id, translate, StringComparer.Ordinal);
}
