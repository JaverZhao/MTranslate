using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MTranslate.Core;

namespace MTranslate.DocumentFormats;

public sealed class DocumentTranslator(
    TranslationService translationService,
    DocumentParserRegistry parserRegistry,
    IDocumentCheckpointStore checkpointStore,
    ITokenEstimator? tokenEstimator = null) : IDocumentTranslator
{
    private const int SubtitleBatchSize = 20;
    private readonly ITokenEstimator tokenEstimator = tokenEstimator ?? new HeuristicTokenEstimator();
    private readonly ILanguageDetector languageDetector = new HeuristicLanguageDetector();

    public async Task<DocumentTranslationResult> TranslateAsync(
        DocumentTranslationRequest request,
        IProgress<DocumentTranslationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        var inputPath = Path.GetFullPath(request.InputPath);
        var outputPath = Path.GetFullPath(request.OutputPath);
        var temporaryOutput = outputPath + ".tmp";
        var jobId = request.JobId ?? Guid.NewGuid();
        var parser = parserRegistry.Resolve(inputPath);
        var fileHash = await ComputeFileHashAsync(inputPath, cancellationToken).ConfigureAwait(false);

        ParsedDocument document;
        await using (var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65_536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            document = await parser.ParseAsync(input, cancellationToken).ConfigureAwait(false);

        var segments = document.TranslatableParts;
        var tokenCounts = segments.ToDictionary(segment => segment.Id, segment => tokenEstimator.Estimate(segment.Content), StringComparer.Ordinal);
        var totalTokens = tokenCounts.Values.Sum();
        var checkpoint = await checkpointStore.LoadAsync(jobId, cancellationToken).ConfigureAwait(false);
        var resumed = checkpoint is not null;
        var translations = ValidateAndRestoreCheckpoint(checkpoint, fileHash, request, temporaryOutput);
        var completedTokens = segments.Where(segment => translations.ContainsKey(segment.Id)).Sum(segment => tokenCounts[segment.Id]);
        ReportProgress();
        var stopwatch = Stopwatch.StartNew();

        var pending = segments.Where(segment => !translations.ContainsKey(segment.Id)).ToArray();
        var sourceLanguage = request.SourceLanguage;
        if (pending.Length > 0 && sourceLanguage is null)
        {
            sourceLanguage = languageDetector.Detect(string.Join('\n', segments.Select(segment => segment.Content)))?.LanguageCode
                ?? throw new InvalidOperationException("Unable to detect the document source language; select it explicitly.");
        }
        if (document.Format is DocumentFormat.Srt or DocumentFormat.Vtt)
        {
            foreach (var batch in pending.Chunk(SubtitleBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batchTranslations = await TranslateSubtitleBatchAsync(batch, request, sourceLanguage!, cancellationToken).ConfigureAwait(false);
                foreach (var item in batchTranslations)
                {
                    translations[item.Key] = item.Value;
                    completedTokens += tokenCounts[item.Key];
                }
                await SaveCheckpointAsync().ConfigureAwait(false);
                ReportProgress();
            }
        }
        else
        {
            foreach (var segment in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                translations[segment.Id] = await TranslateSegmentAsync(segment.Content, request, sourceLanguage!, cancellationToken).ConfigureAwait(false);
                completedTokens += tokenCounts[segment.Id];
                await SaveCheckpointAsync().ConfigureAwait(false);
                ReportProgress();
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await using (var output = new FileStream(temporaryOutput, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 65_536,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await parser.WriteAsync(
                document,
                translations,
                output,
                new DocumentWriteOptions(request.SubtitleOutput),
                cancellationToken).ConfigureAwait(false);
            output.Position = 0;
            await parser.ValidateAsync(document, output, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryOutput, outputPath, overwrite: false);
        await checkpointStore.DeleteAsync(jobId, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        return new DocumentTranslationResult(jobId, outputPath, segments.Count, totalTokens, stopwatch.Elapsed, resumed);

        void ReportProgress() => progress?.Report(new DocumentTranslationProgress(
            completedTokens,
            totalTokens,
            translations.Count,
            segments.Count));

        Task SaveCheckpointAsync() => checkpointStore.SaveAsync(new DocumentTranslationCheckpoint(
            jobId,
            fileHash,
            request.TargetLanguage,
            request.ModelProfile,
            temporaryOutput,
            new Dictionary<string, string>(translations, StringComparer.Ordinal),
            DateTimeOffset.UtcNow), cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, string>> TranslateSubtitleBatchAsync(
        IReadOnlyList<DocumentPart> segments,
        DocumentTranslationRequest request,
        string sourceLanguage,
        CancellationToken cancellationToken)
    {
        if (segments.Count == 1)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [segments[0].Id] = await TranslateSegmentAsync(segments[0].Content, request, sourceLanguage, cancellationToken).ConfigureAwait(false)
            };
        }

        var batchText = new StringBuilder();
        foreach (var segment in segments)
        {
            batchText.Append("<mtranslate-segment id=\"").Append(segment.Id).Append("\">\n")
                .Append(segment.Content).Append("\n</mtranslate-segment>\n");
        }
        var translatedBatch = await TranslateSegmentAsync(batchText.ToString(), request, sourceLanguage, cancellationToken).ConfigureAwait(false);
        var translations = ParseBatch(translatedBatch, segments);
        foreach (var segment in segments)
        {
            if (!translations.ContainsKey(segment.Id))
                translations[segment.Id] = await TranslateSegmentAsync(segment.Content, request, sourceLanguage, cancellationToken).ConfigureAwait(false);
        }
        return translations;
    }

    private async Task<string> TranslateSegmentAsync(
        string text,
        DocumentTranslationRequest request,
        string sourceLanguage,
        CancellationToken cancellationToken)
    {
        var result = await translationService.TranslateAsync(new TranslationServiceRequest(
            text,
            request.TargetLanguage,
            sourceLanguage,
            request.ModelProfile,
            request.GlossaryVersion,
            Source: TranslationJobSource.File,
            Priority: TranslationJobPriority.Low), cancellationToken).ConfigureAwait(false);
        return result.Text.Trim();
    }

    private static Dictionary<string, string> ParseBatch(string translated, IReadOnlyList<DocumentPart> expected)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var segment in expected)
        {
            var opening = $"<mtranslate-segment id=\"{segment.Id}\">";
            const string closing = "</mtranslate-segment>";
            var start = translated.IndexOf(opening, StringComparison.Ordinal);
            if (start < 0)
                continue;
            start += opening.Length;
            var end = translated.IndexOf(closing, start, StringComparison.Ordinal);
            if (end < 0)
                continue;
            var value = translated[start..end].Trim();
            if (value.Length > 0)
                result[segment.Id] = value;
        }
        return result;
    }

    private static Dictionary<string, string> ValidateAndRestoreCheckpoint(
        DocumentTranslationCheckpoint? checkpoint,
        string fileHash,
        DocumentTranslationRequest request,
        string temporaryOutput)
    {
        if (checkpoint is null)
            return new Dictionary<string, string>(StringComparer.Ordinal);
        if (!checkpoint.FileHash.Equals(fileHash, StringComparison.OrdinalIgnoreCase)
            || !checkpoint.TargetLanguage.Equals(request.TargetLanguage, StringComparison.OrdinalIgnoreCase)
            || !checkpoint.ModelProfile.Equals(request.ModelProfile, StringComparison.Ordinal)
            || !Path.GetFullPath(checkpoint.OutputTempFile).Equals(Path.GetFullPath(temporaryOutput), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Document checkpoint does not match the current file, language, model, or output path.");
        return new Dictionary<string, string>(checkpoint.CompletedSegments, StringComparer.Ordinal);
    }

    private static async Task<string> ComputeFileHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65_536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static void ValidateRequest(DocumentTranslationRequest request)
    {
        if (!File.Exists(request.InputPath))
            throw new FileNotFoundException("Input document was not found.", request.InputPath);
        if (string.IsNullOrWhiteSpace(request.OutputPath))
            throw new ArgumentException("Output path is required.", nameof(request));
        if (Path.GetFullPath(request.InputPath).Equals(Path.GetFullPath(request.OutputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Document translation never overwrites the source file.");
        if (File.Exists(request.OutputPath))
            throw new IOException("Output file already exists; choose a new path to avoid data loss.");
        if (string.IsNullOrWhiteSpace(request.TargetLanguage))
            throw new ArgumentException("Target language is required.", nameof(request));
    }
}
