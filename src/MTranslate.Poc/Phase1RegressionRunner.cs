using System.Diagnostics;
using System.Security.Cryptography;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MTranslate.Core;
using MTranslate.Infrastructure;

namespace MTranslate.Poc;

internal sealed class Phase1RegressionRunner(
    InferenceRuntimeConfiguration configuration,
    string modelMode,
    string reportPath)
{
    private static readonly TranslationProfile RegressionProfile = TranslationProfile.Default with { MaxTokens = 256 };

    public async Task<Phase1RegressionReport> RunAsync(
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var modelHash = await ComputeSha256Async(configuration.ModelPath, cancellationToken).ConfigureAwait(false);
        var cases = new List<Phase1RegressionCase>();
        var benchmarkResults = new List<TranslationResult>();

        await using var runtime = new LlamaServerRuntime(configuration);
        await runtime.StartAsync(log, cancellationToken).ConfigureAwait(false);
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://{configuration.Host}:{configuration.Port}/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", runtime.ApiKey);
        var client = new LlamaServerTranslationClient(httpClient, new TranslationPromptBuilder());

        cases.Add(await ExecuteTranslationCaseAsync(
            "EnglishToChinese",
            new TranslationRequest("Local translation keeps private text on your computer.", "Chinese", "English", Profile: RegressionProfile),
            client,
            output => output.Any(character => character is >= '\u3400' and <= '\u9fff'),
            "Expected at least one CJK character.",
            cancellationToken).ConfigureAwait(false));

        cases.Add(await ExecuteTranslationCaseAsync(
            "ChineseToEnglish",
            new TranslationRequest("本地翻译可以保护用户的隐私。", "English", "Chinese", Profile: RegressionProfile),
            client,
            output => output.Any(character => character is >= 'A' and <= 'z'),
            "Expected Latin characters in the English translation.",
            cancellationToken).ConfigureAwait(false));

        cases.Add(await ExecuteStreamingCaseAsync(client, cancellationToken).ConfigureAwait(false));
        var benchmarkRequest = new TranslationRequest(
            "The quick brown fox jumps over the lazy dog while the translation engine measures local inference performance.",
            "Chinese",
            "English",
            Profile: RegressionProfile);
        for (var index = 0; index < 3; index++)
            benchmarkResults.Add(await client.TranslateAsync(benchmarkRequest, cancellationToken).ConfigureAwait(false));

        cases.Add(await ExecuteCancellationCaseAsync(client, cancellationToken).ConfigureAwait(false));

        var rates = benchmarkResults
            .Where(result => result.CompletionTokens.HasValue && result.TotalDuration.TotalSeconds > 0)
            .Select(result => result.CompletionTokens!.Value / result.TotalDuration.TotalSeconds)
            .ToArray();
        var report = new Phase1RegressionReport(
            DateTimeOffset.UtcNow,
            modelMode,
            Path.GetFullPath(configuration.ModelPath),
            modelHash,
            new FileInfo(configuration.ModelPath).Length,
            Path.GetFullPath(configuration.ExecutablePath),
            Environment.OSVersion.ToString(),
            Environment.ProcessorCount,
            cases,
            benchmarkResults.Average(result => result.TotalDuration.TotalMilliseconds),
            rates.Length == 0 ? null : rates.Average());

        var fullReportPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullReportPath)!);
        await File.WriteAllTextAsync(
            fullReportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
            Encoding.UTF8,
            cancellationToken).ConfigureAwait(false);
        return report;
    }

    private static async Task<Phase1RegressionCase> ExecuteTranslationCaseAsync(
        string name,
        TranslationRequest request,
        ITranslationClient client,
        Func<string, bool> validator,
        string validationFailure,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await client.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            var passed = !string.IsNullOrWhiteSpace(result.Text) && validator(result.Text);
            return new Phase1RegressionCase(
                name,
                passed,
                stopwatch.Elapsed.TotalMilliseconds,
                passed ? result.Text.Trim() : $"{validationFailure} Output: {result.Text.Trim()}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new Phase1RegressionCase(name, false, stopwatch.Elapsed.TotalMilliseconds, exception.Message);
        }
    }

    private static async Task<Phase1RegressionCase> ExecuteStreamingCaseAsync(
        ITranslationClient client,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var output = new StringBuilder();
        var chunks = 0;
        try
        {
            var request = new TranslationRequest(
                "Streaming translation should display partial results as they arrive.",
                "Chinese",
                "English",
                Profile: RegressionProfile);
            await foreach (var chunk in client.TranslateStreamingAsync(request, cancellationToken).ConfigureAwait(false))
            {
                output.Append(chunk.Text);
                chunks++;
            }
            stopwatch.Stop();
            var passed = chunks > 0 && output.Length > 0;
            return new Phase1RegressionCase(
                "Streaming",
                passed,
                stopwatch.Elapsed.TotalMilliseconds,
                passed ? $"Received {chunks} chunks: {output.ToString().Trim()}" : "No streaming content was received.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new Phase1RegressionCase("Streaming", false, stopwatch.Elapsed.TotalMilliseconds, exception.Message);
        }
    }

    private static async Task<Phase1RegressionCase> ExecuteCancellationCaseAsync(
        ITranslationClient client,
        CancellationToken outerCancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(outerCancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(25));
        try
        {
            var repeatedText = string.Join(' ', Enumerable.Repeat(
                "This deliberately long sentence verifies that an in-flight translation request observes cancellation.",
                100));
            await client.TranslateAsync(
                new TranslationRequest(repeatedText, "Chinese", "English", Profile: RegressionProfile),
                cancellation.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return new Phase1RegressionCase(
                "Cancellation",
                false,
                stopwatch.Elapsed.TotalMilliseconds,
                "The request completed before the cancellation signal was observed.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested && !outerCancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new Phase1RegressionCase(
                "Cancellation",
                true,
                stopwatch.Elapsed.TotalMilliseconds,
                "The in-flight HTTP request observed cancellation.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new Phase1RegressionCase(
                "Cancellation",
                false,
                stopwatch.Elapsed.TotalMilliseconds,
                exception.Message);
        }
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
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }
}

internal sealed record Phase1RegressionReport(
    DateTimeOffset TimestampUtc,
    string ModelMode,
    string ModelPath,
    string ModelSha256,
    long ModelSizeBytes,
    string RuntimePath,
    string OperatingSystem,
    int ProcessorCount,
    IReadOnlyList<Phase1RegressionCase> Cases,
    double AverageLatencyMilliseconds,
    double? AverageCompletionTokensPerSecond);

internal sealed record Phase1RegressionCase(
    string Name,
    bool Passed,
    double DurationMilliseconds,
    string Detail);
