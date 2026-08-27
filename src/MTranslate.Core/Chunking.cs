using System.Text;

namespace MTranslate.Core;

public sealed record ChunkingOptions(int TargetTokens = 1_200, int MaximumTokens = 1_800)
{
    public void Validate()
    {
        if (TargetTokens <= 0)
            throw new ArgumentOutOfRangeException(nameof(TargetTokens));
        if (MaximumTokens < TargetTokens)
            throw new ArgumentOutOfRangeException(nameof(MaximumTokens));
    }
}

public sealed record SourceChunk(int Index, string Text, string SeparatorAfter);

public interface ITokenEstimator
{
    int Estimate(string text);
}

public interface IChunkManager
{
    IReadOnlyList<SourceChunk> Split(string text, ChunkingOptions? options = null);
}

public sealed class HeuristicTokenEstimator : ITokenEstimator
{
    public int Estimate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = 0;
        var inLatinWord = false;

        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                inLatinWord = false;
                continue;
            }

            if (rune.Value <= 0x7f && (Rune.IsLetterOrDigit(rune) || rune.Value is '_' or '-'))
            {
                if (!inLatinWord)
                    tokens++;
                inLatinWord = true;
                continue;
            }

            inLatinWord = false;
            tokens++;
        }

        return Math.Max(tokens, text.Length == 0 ? 0 : 1);
    }
}

public sealed class ChunkManager(ITokenEstimator? tokenEstimator = null) : IChunkManager
{
    private static readonly char[] SentenceEndings = ['.', '?', '!', '。', '？', '！', '；', ';'];
    private readonly ITokenEstimator tokenEstimator = tokenEstimator ?? new HeuristicTokenEstimator();

    public IReadOnlyList<SourceChunk> Split(string text, ChunkingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        options ??= new ChunkingOptions();
        options.Validate();

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Normalize(NormalizationForm.FormC);
        if (string.IsNullOrWhiteSpace(normalized))
            return [];

        var chunks = new List<SourceChunk>();
        var offset = 0;
        while (offset < normalized.Length)
        {
            var remaining = normalized[offset..];
            if (tokenEstimator.Estimate(remaining) <= options.MaximumTokens)
            {
                AddChunk(chunks, remaining);
                break;
            }

            var cut = FindCut(remaining, options);
            AddChunk(chunks, remaining[..cut]);
            offset += cut;
        }

        return chunks;
    }

    private int FindCut(string text, ChunkingOptions options)
    {
        var maximumPosition = FindMaximumPosition(text, options.MaximumTokens);
        var targetPosition = FindMaximumPosition(text[..maximumPosition], options.TargetTokens);

        foreach (var boundary in new[]
                 {
                     FindLast(text, "\n\n", targetPosition, maximumPosition),
                     FindLast(text, "\n", targetPosition, maximumPosition),
                     FindLastSentenceEnd(text, targetPosition, maximumPosition),
                     FindLastWhitespace(text, targetPosition, maximumPosition)
                 })
        {
            if (boundary > 0)
                return boundary;
        }

        var hardBoundary = FindLastWhitespace(text, 1, maximumPosition);
        if (hardBoundary > 0)
            return hardBoundary;

        for (var index = maximumPosition; index < text.Length; index++)
        {
            if (char.IsWhiteSpace(text[index]))
                return index + 1;
        }

        return text.Length;
    }

    private int FindMaximumPosition(string text, int tokenLimit)
    {
        var low = 1;
        var high = text.Length;
        var best = 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (tokenEstimator.Estimate(text[..middle]) <= tokenLimit)
            {
                best = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return best;
    }

    private static int FindLast(string text, string marker, int target, int maximum)
    {
        var index = text[..maximum].LastIndexOf(marker, StringComparison.Ordinal);
        return index >= target ? index + marker.Length : -1;
    }

    private static int FindLastSentenceEnd(string text, int target, int maximum)
    {
        for (var index = maximum - 1; index >= target; index--)
        {
            if (SentenceEndings.Contains(text[index]))
                return index + 1;
        }

        return -1;
    }

    private static int FindLastWhitespace(string text, int target, int maximum)
    {
        for (var index = maximum - 1; index >= target; index--)
        {
            if (char.IsWhiteSpace(text[index]))
                return index + 1;
        }

        return -1;
    }

    private static void AddChunk(List<SourceChunk> chunks, string value)
    {
        var separatorStart = value.Length;
        while (separatorStart > 0 && char.IsWhiteSpace(value[separatorStart - 1]))
            separatorStart--;

        var content = value[..separatorStart];
        var separator = value[separatorStart..];
        if (content.Length == 0)
        {
            if (chunks.Count > 0)
                chunks[^1] = chunks[^1] with { SeparatorAfter = chunks[^1].SeparatorAfter + separator };
            return;
        }

        chunks.Add(new SourceChunk(chunks.Count, content, separator));
    }
}
