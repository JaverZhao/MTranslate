using System.Globalization;

namespace MTranslate.DocumentFormats;

public sealed class VttDocumentParser : StructurePreservingParserBase
{
    public override DocumentFormat Format => DocumentFormat.Vtt;
    public override bool CanHandle(string extension) => extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase);

    public override async Task<ParsedDocument> ParseAsync(Stream input, CancellationToken cancellationToken = default)
    {
        var decoded = await TextDocumentIO.ReadAsync(input, cancellationToken).ConfigureAwait(false);
        var lines = TextLineReader.Read(decoded.Text);
        if (lines.Count == 0 || !lines[0].Content.StartsWith("WEBVTT", StringComparison.Ordinal))
            throw new InvalidDataException("VTT document must start with WEBVTT.");

        var firstBlank = Enumerable.Range(1, Math.Max(0, lines.Count - 1))
            .FirstOrDefault(index => string.IsNullOrWhiteSpace(lines[index].Content), -1);
        var blockStart = firstBlank < 0 ? lines.Count : firstBlank + 1;
        var ranges = new List<TranslatableRange>();
        var cues = new List<SubtitleCue>();
        foreach (var block in TextLineReader.Blocks(lines, blockStart))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = block[0].Content.TrimStart();
            if (first.StartsWith("NOTE", StringComparison.Ordinal)
                || first.Equals("STYLE", StringComparison.Ordinal)
                || first.Equals("REGION", StringComparison.Ordinal))
                continue;

            var timingIndex = block[0].Content.Contains("-->", StringComparison.Ordinal) ? 0 : 1;
            if (timingIndex >= block.Count || !block[timingIndex].Content.Contains("-->", StringComparison.Ordinal))
                throw new InvalidDataException($"Invalid VTT cue near character {block[0].Start}.");
            if (timingIndex + 1 >= block.Count)
                throw new InvalidDataException("VTT cue contains no text.");
            var (start, end, settings) = ParseTiming(block[timingIndex].Content);
            var textStart = block[timingIndex + 1].Start;
            var textEnd = block[^1].ContentEnd;
            var segmentId = $"vtt-{cues.Count:D6}";
            ranges.Add(new TranslatableRange(textStart, textEnd - textStart, segmentId, DocumentPartKind.SubtitleText));
            cues.Add(new SubtitleCue(
                $"cue-{cues.Count:D6}",
                timingIndex == 1 ? block[0].Content : null,
                start,
                end,
                settings,
                segmentId));
        }

        return new ParsedDocument(
            Format,
            decoded.Encoding,
            decoded.HasByteOrderMark,
            BuildParts(decoded.Text, ranges),
            cues,
            cues.ToDictionary(
                cue => cue.Id,
                cue => $"{cue.Identifier}|{cue.Start:c}|{cue.End:c}|{cue.Settings}",
                StringComparer.Ordinal));
    }

    private static (TimeSpan Start, TimeSpan End, string Settings) ParseTiming(string line)
    {
        var separator = line.IndexOf("-->", StringComparison.Ordinal);
        var start = ParseTimestamp(line[..separator].Trim());
        var right = line[(separator + 3)..].Trim();
        var space = right.IndexOf(' ');
        var endText = space < 0 ? right : right[..space];
        var settings = space < 0 ? string.Empty : right[(space + 1)..];
        return (start, ParseTimestamp(endText), settings);
    }

    private static TimeSpan ParseTimestamp(string value)
    {
        var colonParts = value.Split(':');
        if (colonParts.Length is not (2 or 3))
            throw new InvalidDataException($"Invalid VTT timestamp: {value}");
        var hourText = colonParts.Length == 3 ? colonParts[0] : "0";
        var minuteText = colonParts[^2];
        var secondParts = colonParts[^1].Split('.');
        if (secondParts.Length != 2
            || !int.TryParse(hourText, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(minuteText, CultureInfo.InvariantCulture, out var minutes)
            || !int.TryParse(secondParts[0], CultureInfo.InvariantCulture, out var seconds)
            || !int.TryParse(secondParts[1], CultureInfo.InvariantCulture, out var milliseconds)
            || minutes is < 0 or > 59 || seconds is < 0 or > 59 || milliseconds is < 0 or > 999)
            throw new InvalidDataException($"Invalid VTT timestamp: {value}");
        return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(milliseconds);
    }
}
