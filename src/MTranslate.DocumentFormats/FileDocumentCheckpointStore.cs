using System.Text.Json;

namespace MTranslate.DocumentFormats;

public sealed class FileDocumentCheckpointStore : IDocumentCheckpointStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string directory;

    public FileDocumentCheckpointStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Checkpoint directory is required.", nameof(directory));
        this.directory = Path.GetFullPath(directory);
    }

    public async Task<DocumentTranslationCheckpoint?> LoadAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var path = GetPath(jobId);
        if (!File.Exists(path))
            return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16_384, FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<DocumentTranslationCheckpoint>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Checkpoint '{path}' is empty or invalid.");
    }

    public async Task SaveAsync(DocumentTranslationCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Directory.CreateDirectory(directory);
        var path = GetPath(checkpoint.JobId);
        var temporary = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 16_384,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, checkpoint, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
            throw;
        }
    }

    public Task DeleteAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(jobId);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetPath(Guid jobId) => Path.Combine(directory, $"{jobId:N}.json");
}
