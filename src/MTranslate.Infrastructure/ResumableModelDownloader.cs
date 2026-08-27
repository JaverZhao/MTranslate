using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using MTranslate.Core;

namespace MTranslate.Infrastructure;

public sealed class ResumableModelDownloader(HttpClient httpClient) : IModelDownloader
{
    public async Task DownloadAsync(
        Uri source,
        string destinationPath,
        string expectedSha256,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.IsAbsoluteUri || source.Scheme is not ("http" or "https"))
            throw new ArgumentException("Model source must be an absolute HTTP or HTTPS URI.", nameof(source));
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path must not be empty.", nameof(destinationPath));
        var normalizedHash = NormalizeSha256(expectedSha256);

        var fullDestination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullDestination)
            ?? throw new ArgumentException("Destination path has no parent directory.", nameof(destinationPath));
        Directory.CreateDirectory(directory);
        var partialPath = fullDestination + ".part";
        var existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0L;

        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        if (existingLength > 0)
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var resumed = existingLength > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed)
            existingLength = 0;
        var responseLength = response.Content.Headers.ContentLength;
        long? totalLength = responseLength.HasValue ? existingLength + responseLength.Value : null;

        await using (var output = new FileStream(
            partialPath,
            resumed ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
            try
            {
                var downloaded = existingLength;
                progress?.Report(new DownloadProgress(downloaded, totalLength));
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    downloaded += read;
                    progress?.Report(new DownloadProgress(downloaded, totalLength));
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        var actualHash = await ComputeSha256Async(partialPath, cancellationToken).ConfigureAwait(false);
        if (!actualHash.Equals(normalizedHash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partialPath);
            throw new InvalidDataException($"Model SHA256 mismatch. Expected {normalizedHash}, actual {actualHash}.");
        }

        File.Move(partialPath, fullDestination, overwrite: true);
    }

    private static string NormalizeSha256(string value)
    {
        var normalized = value.Trim().Replace("-", string.Empty, StringComparison.Ordinal);
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Expected SHA256 must contain exactly 64 hexadecimal characters.", nameof(value));
        return normalized.ToUpperInvariant();
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
