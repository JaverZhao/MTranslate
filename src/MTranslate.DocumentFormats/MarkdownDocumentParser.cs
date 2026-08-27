namespace MTranslate.DocumentFormats;

public sealed class MarkdownDocumentParser : StructurePreservingParserBase
{
    public override DocumentFormat Format => DocumentFormat.Markdown;
    public override bool CanHandle(string extension) =>
        extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase);

    public override async Task<ParsedDocument> ParseAsync(Stream input, CancellationToken cancellationToken = default)
    {
        var decoded = await TextDocumentIO.ReadAsync(input, cancellationToken).ConfigureAwait(false);
        var ranges = new List<TranslatableRange>();
        var lines = TextLineReader.Read(decoded.Text);
        var inFrontMatter = lines.Count > 0 && lines[0].Content.Trim() == "---";
        var frontMatterComplete = !inFrontMatter;
        string? fence = null;

        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = lines[lineIndex];
            var trimmed = line.Content.TrimStart();
            if (lineIndex == 0 && inFrontMatter)
                continue;
            if (inFrontMatter && !frontMatterComplete)
            {
                if (trimmed is "---" or "...")
                {
                    frontMatterComplete = true;
                    continue;
                }
                AddFrontMatterValue(line, ranges);
                continue;
            }

            var fenceMarker = GetFenceMarker(trimmed);
            if (fence is not null)
            {
                if (fenceMarker == fence)
                    fence = null;
                continue;
            }
            if (fenceMarker is not null)
            {
                fence = fenceMarker;
                continue;
            }

            AddMarkdownTextRanges(line, ranges);
        }

        if (inFrontMatter && !frontMatterComplete)
            throw new InvalidDataException("Markdown front matter is not terminated.");
        if (fence is not null)
            throw new InvalidDataException("Markdown fenced code block is not terminated.");

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

    private static void AddFrontMatterValue(TextLine line, List<TranslatableRange> ranges)
    {
        var trimmed = line.Content.TrimStart();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            return;
        var colon = line.Content.IndexOf(':');
        if (colon <= 0)
            return;
        var start = colon + 1;
        while (start < line.Content.Length && char.IsWhiteSpace(line.Content[start]))
            start++;
        var end = line.Content.Length;
        var comment = FindUnquotedComment(line.Content, start);
        if (comment >= 0)
            end = comment;
        while (end > start && char.IsWhiteSpace(line.Content[end - 1]))
            end--;
        if (end > start)
            ranges.Add(new TranslatableRange(line.Start + start, end - start, $"md-{ranges.Count:D6}", DocumentPartKind.MetadataValue));
    }

    private static int FindUnquotedComment(string text, int start)
    {
        var quote = '\0';
        for (var index = start; index < text.Length; index++)
        {
            if (text[index] is '\'' or '"')
            {
                quote = quote == '\0' ? text[index] : quote == text[index] ? '\0' : quote;
                continue;
            }
            if (text[index] == '#' && quote == '\0' && (index == start || char.IsWhiteSpace(text[index - 1])))
                return index;
        }
        return -1;
    }

    private static string? GetFenceMarker(string trimmed)
    {
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
            return "```";
        if (trimmed.StartsWith("~~~", StringComparison.Ordinal))
            return "~~~";
        return null;
    }

    private static void AddMarkdownTextRanges(TextLine line, List<TranslatableRange> ranges)
    {
        if (string.IsNullOrWhiteSpace(line.Content))
            return;
        var protectedIntervals = new List<(int Start, int End)>();
        ProtectPrefix(line.Content, protectedIntervals);
        ProtectDelimited(line.Content, '`', protectedIntervals);
        ProtectAngleTags(line.Content, protectedIntervals);
        ProtectUrls(line.Content, protectedIntervals);
        ProtectLinkDestinations(line.Content, protectedIntervals);
        ProtectSyntaxCharacters(line.Content, protectedIntervals);

        var merged = MergeIntervals(protectedIntervals, line.Content.Length);
        var position = 0;
        foreach (var interval in merged.Append((Start: line.Content.Length, End: line.Content.Length)))
        {
            AddVisibleRange(position, interval.Start);
            position = Math.Max(position, interval.End);
        }

        void AddVisibleRange(int start, int end)
        {
            while (start < end && char.IsWhiteSpace(line.Content[start])) start++;
            while (end > start && char.IsWhiteSpace(line.Content[end - 1])) end--;
            if (end <= start || !line.Content.AsSpan(start, end - start).ContainsAnyInRange('A', 'z')
                && !line.Content.AsSpan(start, end - start).ContainsAnyInRange('\u00c0', '\uffff'))
                return;
            ranges.Add(new TranslatableRange(line.Start + start, end - start, $"md-{ranges.Count:D6}"));
        }
    }

    private static void ProtectPrefix(string line, List<(int Start, int End)> intervals)
    {
        var index = 0;
        while (index < line.Length && char.IsWhiteSpace(line[index])) index++;
        var prefixEnd = index;
        while (prefixEnd < line.Length && line[prefixEnd] == '>')
        {
            prefixEnd++;
            if (prefixEnd < line.Length && line[prefixEnd] == ' ') prefixEnd++;
        }
        if (prefixEnd < line.Length && line[prefixEnd] == '#')
        {
            while (prefixEnd < line.Length && line[prefixEnd] == '#') prefixEnd++;
            if (prefixEnd < line.Length && line[prefixEnd] == ' ') prefixEnd++;
        }
        else if (prefixEnd + 1 < line.Length && line[prefixEnd] is '-' or '*' or '+' && line[prefixEnd + 1] == ' ')
        {
            prefixEnd += 2;
        }
        else
        {
            var digitEnd = prefixEnd;
            while (digitEnd < line.Length && char.IsDigit(line[digitEnd])) digitEnd++;
            if (digitEnd + 1 < line.Length && line[digitEnd] is '.' or ')' && line[digitEnd + 1] == ' ')
                prefixEnd = digitEnd + 2;
        }
        if (prefixEnd > 0)
            intervals.Add((0, prefixEnd));
    }

    private static void ProtectDelimited(string line, char delimiter, List<(int Start, int End)> intervals)
    {
        var index = 0;
        while (index < line.Length)
        {
            var start = line.IndexOf(delimiter, index);
            if (start < 0) return;
            var end = line.IndexOf(delimiter, start + 1);
            if (end < 0) return;
            intervals.Add((start, end + 1));
            index = end + 1;
        }
    }

    private static void ProtectAngleTags(string line, List<(int Start, int End)> intervals)
    {
        var index = 0;
        while ((index = line.IndexOf('<', index)) >= 0)
        {
            var end = line.IndexOf('>', index + 1);
            if (end < 0) return;
            intervals.Add((index, end + 1));
            index = end + 1;
        }
    }

    private static void ProtectUrls(string line, List<(int Start, int End)> intervals)
    {
        foreach (var prefix in new[] { "https://", "http://", "www." })
        {
            var index = 0;
            while ((index = line.IndexOf(prefix, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var end = index + prefix.Length;
                while (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] is not '<' and not '>') end++;
                while (end > index && line[end - 1] is '.' or ',' or ';') end--;
                intervals.Add((index, end));
                index = end;
            }
        }
    }

    private static void ProtectLinkDestinations(string line, List<(int Start, int End)> intervals)
    {
        var index = 0;
        while ((index = line.IndexOf("](", index, StringComparison.Ordinal)) >= 0)
        {
            var end = line.IndexOf(')', index + 2);
            if (end < 0) return;
            intervals.Add((index, end + 1));
            index = end + 1;
        }
    }

    private static void ProtectSyntaxCharacters(string line, List<(int Start, int End)> intervals)
    {
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] is '*' or '_' or '~' or '[' or ']' or '!' or '\\')
                intervals.Add((index, index + 1));
        }
    }

    private static IReadOnlyList<(int Start, int End)> MergeIntervals(
        IEnumerable<(int Start, int End)> intervals,
        int length)
    {
        var ordered = intervals
            .Select(interval => (Start: Math.Clamp(interval.Start, 0, length), End: Math.Clamp(interval.End, 0, length)))
            .Where(interval => interval.End > interval.Start)
            .OrderBy(interval => interval.Start)
            .ToArray();
        var merged = new List<(int Start, int End)>();
        foreach (var interval in ordered)
        {
            if (merged.Count == 0 || interval.Start > merged[^1].End)
                merged.Add(interval);
            else
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, interval.End));
        }
        return merged;
    }
}
