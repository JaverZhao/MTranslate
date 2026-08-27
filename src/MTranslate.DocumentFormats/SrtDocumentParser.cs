using System.Globalization;

namespace MTranslate.DocumentFormats;

public sealed class SrtDocumentParser : StructurePreservingParserBase
{
    public override DocumentFormat Format => DocumentFormat.Srt;
    public override bool CanHandle(string extension) => extension.Equals(".srt", StringComparison.OrdinalIgnoreCase);

    public override async Task<ParsedDocument> ParseAsync(Stream input, CancellationToken cancellationToken = default)
    {
        var decoded = await TextDocumentIO.ReadAsync(input, cancellationToken).ConfigureAwait(false);
        var ranges = new List<TranslatableRange>();
        var cues = new List<SubtitleCue>();
        var blocks = TextLineReader.Blocks(TextLineReader.Read(decoded.Text));
        foreach (var block in blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (block.Count < 3 || !int.TryParse(block[0].Content.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                throw new InvalidDataException($"Invalid SRT cue near character {block[0].Start}.");
            var (start, end) = ParseTiming(block[1].Content);
            var textStart = block[2].Start;
            var textEnd = block[^1].ContentEnd;
            var segmentId = $"srt-{cues.Count:D6}";
            ranges.Add(new TranslatableRange(textStart, textEnd - textStart, segmentId, DocumentPartKind.SubtitleText));
            cues.Add(new SubtitleCue(
                $"cue-{cues.Count:D6}",
                block[0].Content,
                start,
                end,
                string.Empty,
                segmentId));
        }
        if (cues.Count == 0 && !string.IsNullOrWhiteSpace(decoded.Text))
            throw new InvalidDataException("SRT document contains no valid cues.");

        return new ParsedDocument(
            Format,
            decoded.Encoding,
            decoded.HasByteOrderMark,
            BuildParts(decoded.Text, ranges),
            cues,
            CreateCueMetadata(cues));
    }

    private static (TimeSpan Start, TimeSpan End) ParseTiming(string line)
    {
        var separator = line.IndexOf("-->", StringComparison.Ordinal);
        if (separator < 0)
            throw new InvalidDataException($"Invalid SRT timing line: {line}");
        return (ParseTimestamp(line[..separator].Trim()), ParseTimestamp(line[(separator + 3)..].Trim()));
    }

    private static TimeSpan ParseTimestamp(string value)
    {
        var fields = value.Split([':', ',']);
        if (fields.Length != 4
            || !int.TryParse(fields[0], CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(fields[1], CultureInfo.InvariantCulture, out var minutes)
            || !int.TryParse(fields[2], CultureInfo.InvariantCulture, out var seconds)
            || !int.TryParse(fields[3], CultureInfo.InvariantCulture, out var milliseconds)
            || minutes is < 0 or > 59 || seconds is < 0 or > 59 || milliseconds is < 0 or > 999)
            throw new InvalidDataException($"Invalid SRT timestamp: {value}");
        return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(milliseconds);
    }

    private static IReadOnlyDictionary<string, string> CreateCueMetadata(IEnumerable<SubtitleCue> cues) =>
        cues.ToDictionary(
            cue => cue.Id,
            cue => $"{cue.Identifier}|{cue.Start:c}|{cue.End:c}",
            StringComparer.Ordinal);
}
