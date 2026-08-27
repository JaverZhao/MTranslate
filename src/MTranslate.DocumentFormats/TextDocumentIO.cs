using System.Text;

namespace MTranslate.DocumentFormats;

internal sealed record DecodedDocument(string Text, Encoding Encoding, bool HasByteOrderMark);

internal static class TextDocumentIO
{
    public static async Task<DecodedDocument> ReadAsync(Stream input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var memory = new MemoryStream();
        await input.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        var bytes = memory.ToArray();
        var (encoding, preambleLength) = DetectEncoding(bytes);
        var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        return new DecodedDocument(text, encoding, preambleLength > 0);
    }

    public static async Task WriteAsync(
        Stream output,
        string text,
        Encoding encoding,
        bool includeByteOrderMark,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (includeByteOrderMark)
        {
            var preamble = encoding.GetPreamble();
            await output.WriteAsync(preamble, cancellationToken).ConfigureAwait(false);
        }
        var bytes = encoding.GetBytes(text);
        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static (Encoding Encoding, int PreambleLength) DetectEncoding(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()))
            return (new UTF8Encoding(true, true), 3);
        if (bytes.AsSpan().StartsWith(Encoding.UTF32.GetPreamble()))
            return (new UTF32Encoding(false, true, true), 4);
        var bigEndianUtf32 = new UTF32Encoding(true, true, true);
        if (bytes.AsSpan().StartsWith(bigEndianUtf32.GetPreamble()))
            return (bigEndianUtf32, 4);
        if (bytes.AsSpan().StartsWith(Encoding.Unicode.GetPreamble()))
            return (new UnicodeEncoding(false, true, true), 2);
        if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.GetPreamble()))
            return (new UnicodeEncoding(true, true, true), 2);
        return (new UTF8Encoding(false, true), 0);
    }
}
