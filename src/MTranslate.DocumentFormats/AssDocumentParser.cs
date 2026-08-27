namespace MTranslate.DocumentFormats;

public sealed class AssDocumentParser : StructurePreservingParserBase
{
    public override DocumentFormat Format => DocumentFormat.Ass;
    public override bool CanHandle(string extension) => extension.Equals(".ass", StringComparison.OrdinalIgnoreCase);

    public override async Task<ParsedDocument> ParseAsync(Stream input, CancellationToken cancellationToken = default)
    {
        var decoded = await TextDocumentIO.ReadAsync(input, cancellationToken).ConfigureAwait(false);
        var lines = TextLineReader.Read(decoded.Text);
        var inEvents = false;
        string[]? fields = null;
        var ranges = new List<TranslatableRange>();

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trimmed = line.Content.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                inEvents = trimmed.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inEvents)
                continue;
            if (trimmed.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
            {
                fields = trimmed[7..].Split(',').Select(field => field.Trim()).ToArray();
                if (!fields.Contains("Text", StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException("ASS Events format does not contain a Text field.");
                continue;
            }
            if (!trimmed.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (fields is null)
                throw new InvalidDataException("ASS Dialogue appears before the Events Format line.");

            var textField = Array.FindIndex(fields, field => field.Equals("Text", StringComparison.OrdinalIgnoreCase));
            var contentOffset = line.Content.IndexOf(':') + 1;
            while (contentOffset < line.Content.Length && line.Content[contentOffset] == ' ') contentOffset++;
            var textStart = FindFieldStart(line.Content, contentOffset, textField);
            var textEnd = FindFieldEnd(line.Content, textStart, fields.Length - textField - 1);
            AddVisibleAssRanges(line, textStart, textEnd, ranges);
        }

        if (fields is null)
            throw new InvalidDataException("ASS document contains no Events Format line.");
        var parts = BuildParts(decoded.Text, ranges);
        return new ParsedDocument(
            Format,
            decoded.Encoding,
            decoded.HasByteOrderMark,
            parts,
            ProtectedMetadata: new Dictionary<string, string>
            {
                ["structure-hash"] = ComputeStructureHash(parts)
            });
    }

    private static int FindFieldStart(string line, int contentStart, int fieldIndex)
    {
        var position = contentStart;
        for (var index = 0; index < fieldIndex; index++)
        {
            position = line.IndexOf(',', position);
            if (position < 0)
                throw new InvalidDataException("ASS Dialogue has fewer fields than its Format line.");
            position++;
        }
        return position;
    }

    private static int FindFieldEnd(string line, int textStart, int fieldsAfterText)
    {
        if (fieldsAfterText == 0)
            return line.Length;
        var position = line.Length;
        for (var index = 0; index < fieldsAfterText; index++)
        {
            position = line.LastIndexOf(',', position - 1);
            if (position < textStart)
                throw new InvalidDataException("ASS Dialogue has fewer fields than its Format line.");
        }
        return position;
    }

    private static void AddVisibleAssRanges(TextLine line, int start, int end, List<TranslatableRange> ranges)
    {
        var position = start;
        while (position < end)
        {
            if (line.Content[position] == '{')
            {
                var tagEnd = line.Content.IndexOf('}', position + 1);
                if (tagEnd < 0 || tagEnd >= end)
                    throw new InvalidDataException("ASS override tag is not terminated.");
                position = tagEnd + 1;
                continue;
            }
            if (line.Content[position] == '\\' && position + 1 < end && line.Content[position + 1] is 'N' or 'n' or 'h')
            {
                position += 2;
                continue;
            }

            var visibleStart = position;
            while (position < end
                   && line.Content[position] != '{'
                   && !(line.Content[position] == '\\' && position + 1 < end && line.Content[position + 1] is 'N' or 'n' or 'h'))
                position++;
            var visibleEnd = position;
            while (visibleStart < visibleEnd && char.IsWhiteSpace(line.Content[visibleStart])) visibleStart++;
            while (visibleEnd > visibleStart && char.IsWhiteSpace(line.Content[visibleEnd - 1])) visibleEnd--;
            if (visibleEnd > visibleStart)
                ranges.Add(new TranslatableRange(
                    line.Start + visibleStart,
                    visibleEnd - visibleStart,
                    $"ass-{ranges.Count:D6}",
                    DocumentPartKind.SubtitleText));
        }
    }
}
