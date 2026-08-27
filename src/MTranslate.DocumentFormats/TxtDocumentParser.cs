namespace MTranslate.DocumentFormats;

public sealed class TxtDocumentParser : StructurePreservingParserBase
{
    public override DocumentFormat Format => DocumentFormat.Txt;
    public override bool CanHandle(string extension) => extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);

    public override async Task<ParsedDocument> ParseAsync(Stream input, CancellationToken cancellationToken = default)
    {
        var decoded = await TextDocumentIO.ReadAsync(input, cancellationToken).ConfigureAwait(false);
        var ranges = new List<TranslatableRange>();
        foreach (var line in TextLineReader.Read(decoded.Text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line.Content))
                continue;
            var contentStart = 0;
            while (contentStart < line.Content.Length && char.IsWhiteSpace(line.Content[contentStart]))
                contentStart++;
            var contentEnd = line.Content.Length;
            while (contentEnd > contentStart && char.IsWhiteSpace(line.Content[contentEnd - 1]))
                contentEnd--;
            if (contentEnd > contentStart)
            {
                ranges.Add(new TranslatableRange(
                    line.Start + contentStart,
                    contentEnd - contentStart,
                    $"txt-line-{ranges.Count:D6}"));
            }
        }

        return new ParsedDocument(
            Format,
            decoded.Encoding,
            decoded.HasByteOrderMark,
            BuildParts(decoded.Text, ranges));
    }
}
