namespace MTranslate.DocumentFormats;

internal sealed record TranslatableRange(
    int Start,
    int Length,
    string Id,
    DocumentPartKind Kind = DocumentPartKind.Text);

public abstract class StructurePreservingParserBase : IDocumentParser
{
    public abstract DocumentFormat Format { get; }
    public abstract bool CanHandle(string extension);
    public abstract Task<ParsedDocument> ParseAsync(Stream input, CancellationToken cancellationToken = default);

    public virtual async Task WriteAsync(
        ParsedDocument document,
        IReadOnlyDictionary<string, string> translations,
        Stream output,
        DocumentWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(translations);
        options ??= new DocumentWriteOptions();
        var text = new System.Text.StringBuilder();
        var newLine = DetectNewLine(document.OriginalText);
        foreach (var part in document.Parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!part.IsTranslatable || !translations.TryGetValue(part.Id, out var translation))
            {
                text.Append(part.Content);
                continue;
            }

            if (part.Kind == DocumentPartKind.SubtitleText)
            {
                text.Append(options.SubtitleOutput switch
                {
                    SubtitleOutputMode.TranslationOnly => translation,
                    SubtitleOutputMode.OriginalThenTranslation => part.Content + newLine + translation,
                    SubtitleOutputMode.TranslationThenOriginal => translation + newLine + part.Content,
                    _ => throw new ArgumentOutOfRangeException(nameof(options))
                });
            }
            else
            {
                text.Append(translation);
            }
        }

        await TextDocumentIO.WriteAsync(
            output,
            text.ToString(),
            document.Encoding,
            document.HasByteOrderMark,
            cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task ValidateAsync(
        ParsedDocument source,
        Stream translatedOutput,
        CancellationToken cancellationToken = default)
    {
        var translated = await ParseAsync(translatedOutput, cancellationToken).ConfigureAwait(false);
        if (translated.Format != source.Format)
            throw new InvalidDataException("Translated document format changed during writing.");
        if (source.ProtectedMetadata is null)
            return;
        if (translated.ProtectedMetadata is null || source.ProtectedMetadata.Count != translated.ProtectedMetadata.Count)
            throw new InvalidDataException("Protected document structure changed during translation.");
        foreach (var item in source.ProtectedMetadata)
        {
            if (!translated.ProtectedMetadata.TryGetValue(item.Key, out var value) || value != item.Value)
                throw new InvalidDataException($"Protected document field '{item.Key}' changed during translation.");
        }
    }

    internal static IReadOnlyList<DocumentPart> BuildParts(string text, IEnumerable<TranslatableRange> ranges)
    {
        var ordered = ranges.OrderBy(range => range.Start).ToArray();
        var parts = new List<DocumentPart>();
        var position = 0;
        foreach (var range in ordered)
        {
            if (range.Start < position || range.Start < 0 || range.Length < 0 || range.Start + range.Length > text.Length)
                throw new InvalidDataException("Parser produced overlapping or invalid text ranges.");
            if (range.Start > position)
                parts.Add(new DocumentPart($"structure-{parts.Count:D6}", text[position..range.Start], false));
            parts.Add(new DocumentPart(range.Id, text.Substring(range.Start, range.Length), true, range.Kind));
            position = range.Start + range.Length;
        }
        if (position < text.Length)
            parts.Add(new DocumentPart($"structure-{parts.Count:D6}", text[position..], false));
        return parts;
    }

    internal static string DetectNewLine(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    internal static string ComputeStructureHash(IReadOnlyList<DocumentPart> parts)
    {
        var structure = string.Concat(parts.Where(part => !part.IsTranslatable).Select(part => part.Content));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(structure)));
    }
}

internal sealed record TextLine(int Start, int ContentLength, int TotalLength, string Content)
{
    public int ContentEnd => Start + ContentLength;
}

internal static class TextLineReader
{
    public static IReadOnlyList<TextLine> Read(string text)
    {
        var lines = new List<TextLine>();
        var start = 0;
        while (start < text.Length)
        {
            var lineFeed = text.IndexOf('\n', start);
            if (lineFeed < 0)
            {
                lines.Add(new TextLine(start, text.Length - start, text.Length - start, text[start..]));
                return lines;
            }
            var contentEnd = lineFeed > start && text[lineFeed - 1] == '\r' ? lineFeed - 1 : lineFeed;
            lines.Add(new TextLine(start, contentEnd - start, lineFeed - start + 1, text[start..contentEnd]));
            start = lineFeed + 1;
        }
        if (text.Length == 0 || text.EndsWith('\n'))
            lines.Add(new TextLine(text.Length, 0, 0, string.Empty));
        return lines;
    }

    public static IReadOnlyList<IReadOnlyList<TextLine>> Blocks(IReadOnlyList<TextLine> lines, int startIndex = 0)
    {
        var blocks = new List<IReadOnlyList<TextLine>>();
        var current = new List<TextLine>();
        for (var index = startIndex; index < lines.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index].Content))
            {
                if (current.Count > 0)
                {
                    blocks.Add(current.ToArray());
                    current.Clear();
                }
            }
            else
            {
                current.Add(lines[index]);
            }
        }
        if (current.Count > 0)
            blocks.Add(current.ToArray());
        return blocks;
    }
}
