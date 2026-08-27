using System.Diagnostics;
using System.Globalization;
using System.Text;
using MTranslate.Core;
using MTranslate.Infrastructure;
using MTranslate.DocumentFormats;

namespace MTranslate.Poc;

internal static class Program
{
    private const string Usage = """
        MTranslate Phase 1 inference POC

        Commands:
          translate --server <url> --target <language> [--source <language>] [--text <text>] [--context <text>] [--stream] [--api-key <key>]
          translate-file --server <url> --input <path> --output <path> --target <language> [--source <language>] [--output-mode <translation|original-translation|translation-original>] [--api-key <key>]
          benchmark --server <url> --target <language> [--source <language>] [--text <text>] [--iterations <count>] [--api-key <key>]
          download-model --url <url> --sha256 <hash> --output <path>
          run-server --exe <path> --model <path> [--port <port>] [--context-size <tokens>] [--parallel <slots>] [--api-key <key>]
          verify --exe <path> --model <path> --mode <name> [--report <path>] [--port <port>]

        If --text is omitted, translate and benchmark read UTF-8 text from standard input.
        """;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            var options = CommandOptions.Parse(args.Skip(1));
            return args[0] switch
            {
                "translate" => await TranslateAsync(options, cancellation.Token).ConfigureAwait(false),
                "translate-file" => await TranslateFileAsync(options, cancellation.Token).ConfigureAwait(false),
                "benchmark" => await BenchmarkAsync(options, cancellation.Token).ConfigureAwait(false),
                "download-model" => await DownloadModelAsync(options, cancellation.Token).ConfigureAwait(false),
                "run-server" => await RunServerAsync(options, cancellation.Token).ConfigureAwait(false),
                "verify" => await VerifyAsync(options, cancellation.Token).ConfigureAwait(false),
                _ => throw new CommandLineException($"Unknown command '{args[0]}'.")
            };
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("Operation cancelled.");
            return 130;
        }
        catch (Exception exception) when (exception is CommandLineException or ArgumentException or IOException or HttpRequestException or TimeoutException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> TranslateAsync(CommandOptions options, CancellationToken cancellationToken)
    {
        var request = await CreateRequestAsync(options, cancellationToken).ConfigureAwait(false);
        using var httpClient = CreateHttpClient(options.Required("server"), options.Optional("api-key"));
        var client = new LlamaServerTranslationClient(httpClient, new TranslationPromptBuilder());

        if (options.HasFlag("stream"))
        {
            await foreach (var chunk in client.TranslateStreamingAsync(request, cancellationToken).ConfigureAwait(false))
                Console.Write(chunk.Text);
            Console.WriteLine();
            return 0;
        }

        var result = await client.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(result.Text);
        Console.Error.WriteLine(
            $"Duration: {result.TotalDuration.TotalMilliseconds:F0} ms; prompt tokens: {FormatTokenCount(result.PromptTokens)}; completion tokens: {FormatTokenCount(result.CompletionTokens)}");
        return 0;
    }

    private static async Task<int> BenchmarkAsync(CommandOptions options, CancellationToken cancellationToken)
    {
        var request = await CreateRequestAsync(options, cancellationToken).ConfigureAwait(false);
        var iterations = options.PositiveInt("iterations", 3);
        using var httpClient = CreateHttpClient(options.Required("server"), options.Optional("api-key"));
        var client = new LlamaServerTranslationClient(httpClient, new TranslationPromptBuilder());
        var results = new List<TranslationResult>(iterations);

        for (var index = 1; index <= iterations; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await client.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            results.Add(result);
            var tokensPerSecond = CalculateTokensPerSecond(result);
            Console.WriteLine(
                $"Run {index}: {stopwatch.Elapsed.TotalMilliseconds:F0} ms, completion tokens {FormatTokenCount(result.CompletionTokens)}, tokens/s {FormatRate(tokensPerSecond)}");
        }

        var durations = results.Select(result => result.TotalDuration.TotalMilliseconds).ToArray();
        var rates = results.Select(CalculateTokensPerSecond).Where(rate => rate.HasValue).Select(rate => rate!.Value).ToArray();
        Console.WriteLine($"Average latency: {durations.Average():F0} ms");
        Console.WriteLine($"Minimum latency: {durations.Min():F0} ms");
        Console.WriteLine($"Maximum latency: {durations.Max():F0} ms");
        if (rates.Length > 0)
            Console.WriteLine($"Average tokens/s: {rates.Average():F2}");
        return 0;
    }

    private static async Task<int> TranslateFileAsync(CommandOptions options, CancellationToken cancellationToken)
    {
        using var httpClient = CreateHttpClient(options.Required("server"), options.Optional("api-key"));
        var client = new LlamaServerTranslationClient(httpClient, new TranslationPromptBuilder());
        await using var queue = new TranslationJobQueue();
        var service = new TranslationService(client, new ChunkManager(), new NullTranslationCache(), queue);
        var checkpoints = Path.Combine(Path.GetTempPath(), "MTranslate", "poc-checkpoints");
        var translator = new DocumentTranslator(
            service,
            new DocumentParserRegistry(),
            new FileDocumentCheckpointStore(checkpoints));
        var outputMode = (options.Optional("output-mode") ?? "translation") switch
        {
            "translation" => SubtitleOutputMode.TranslationOnly,
            "original-translation" => SubtitleOutputMode.OriginalThenTranslation,
            "translation-original" => SubtitleOutputMode.TranslationThenOriginal,
            var value => throw new CommandLineException($"Unknown subtitle output mode '{value}'.")
        };
        var progress = new Progress<DocumentTranslationProgress>(value =>
        {
            Console.Error.Write($"\rProgress: {value.Percentage,6:F1}% · {value.CompletedSegments}/{value.TotalSegments} segments");
        });
        var result = await translator.TranslateAsync(new DocumentTranslationRequest(
            options.Required("input"),
            options.Required("output"),
            options.Required("target"),
            options.Optional("source"),
            SubtitleOutput: outputMode), progress, cancellationToken).ConfigureAwait(false);
        Console.Error.WriteLine();
        Console.WriteLine($"Translated {result.SegmentCount} segments ({result.SourceTokens} estimated source tokens) to {result.OutputPath}");
        return 0;
    }

    private static async Task<int> DownloadModelAsync(CommandOptions options, CancellationToken cancellationToken)
    {
        var source = new Uri(options.Required("url"), UriKind.Absolute);
        var output = options.Required("output");
        var hash = options.Required("sha256");
        using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var downloader = new ResumableModelDownloader(httpClient);
        var lastLineLength = 0;
        var lastReportedPercent = -1;
        var progress = new Progress<DownloadProgress>(value =>
        {
            var roundedPercent = value.Percentage.HasValue ? (int)Math.Floor(value.Percentage.Value) : -1;
            if (roundedPercent == lastReportedPercent && roundedPercent is >= 0 and < 100)
                return;
            lastReportedPercent = roundedPercent;
            var status = value.Percentage.HasValue
                ? $"Downloaded {FormatBytes(value.BytesDownloaded)} / {FormatBytes(value.TotalBytes!.Value)} ({value.Percentage:F1}%)"
                : $"Downloaded {FormatBytes(value.BytesDownloaded)}";
            Console.Write('\r');
            Console.Write(status.PadRight(lastLineLength));
            lastLineLength = Math.Max(lastLineLength, status.Length);
        });

        await downloader.DownloadAsync(source, output, hash, progress, cancellationToken).ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine($"Model verified and installed at {Path.GetFullPath(output)}");
        return 0;
    }

    private static async Task<int> RunServerAsync(CommandOptions options, CancellationToken cancellationToken)
    {
        var configuration = new InferenceRuntimeConfiguration(
            options.Required("exe"),
            options.Required("model"),
            Port: options.PositiveInt("port", 17892),
            ContextSize: options.PositiveInt("context-size", 8192),
            ParallelSlots: options.PositiveInt("parallel", 2),
            ApiKey: options.Optional("api-key"));
        await using var runtime = new LlamaServerRuntime(configuration);
        await runtime.StartAsync(line => Console.Error.WriteLine($"[llama-server] {line}"), cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"llama-server is ready at http://127.0.0.1:{configuration.Port}/");
        Console.WriteLine("Press Ctrl+C to stop.");
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await runtime.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        return 0;
    }

    private static async Task<int> VerifyAsync(CommandOptions options, CancellationToken cancellationToken)
    {
        var configuration = new InferenceRuntimeConfiguration(
            options.Required("exe"),
            options.Required("model"),
            Port: options.PositiveInt("port", 17892),
            ContextSize: 8192,
            ParallelSlots: 2);
        var reportPath = options.Optional("report") ?? Path.Combine("artifacts", "phase1", "regression-report.json");
        var runner = new Phase1RegressionRunner(configuration, options.Required("mode"), reportPath);
        var report = await runner.RunAsync(
            line => Console.Error.WriteLine($"[llama-server] {line}"),
            cancellationToken).ConfigureAwait(false);

        foreach (var testCase in report.Cases)
            Console.WriteLine($"{testCase.Name}: {(testCase.Passed ? "PASS" : "FAIL")} ({testCase.DurationMilliseconds:F0} ms) {testCase.Detail}");
        Console.WriteLine($"Average latency: {report.AverageLatencyMilliseconds:F0} ms");
        Console.WriteLine($"Average completion tokens/s: {FormatRate(report.AverageCompletionTokensPerSecond)}");
        Console.WriteLine($"Report: {Path.GetFullPath(reportPath)}");
        return report.Cases.All(testCase => testCase.Passed) ? 0 : 2;
    }

    private static async Task<TranslationRequest> CreateRequestAsync(CommandOptions options, CancellationToken cancellationToken)
    {
        var text = options.Optional("text");
        if (text is null)
            text = await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
            throw new CommandLineException("Translation text is empty. Pass --text or pipe text through standard input.");

        return new TranslationRequest(
            text,
            options.Required("target"),
            options.Optional("source"),
            options.Optional("context"));
    }

    private static HttpClient CreateHttpClient(string server, string? apiKey)
    {
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new CommandLineException("--server must be an absolute HTTP or HTTPS URL.");
        var builder = new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/') + "/" };
        var client = new HttpClient { BaseAddress = builder.Uri, Timeout = Timeout.InfiniteTimeSpan };
        if (!string.IsNullOrWhiteSpace(apiKey))
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private static double? CalculateTokensPerSecond(TranslationResult result) =>
        result.CompletionTokens.HasValue && result.TotalDuration.TotalSeconds > 0
            ? result.CompletionTokens.Value / result.TotalDuration.TotalSeconds
            : null;

    private static string FormatTokenCount(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
    private static string FormatRate(double? value) => value?.ToString("F2", CultureInfo.InvariantCulture) ?? "n/a";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:F1} {units[unit]}";
    }

    private sealed class CommandOptions
    {
        private readonly Dictionary<string, string?> values;

        private CommandOptions(Dictionary<string, string?> values) => this.values = values;

        public static CommandOptions Parse(IEnumerable<string> arguments)
        {
            var items = arguments.ToArray();
            var parsed = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var index = 0; index < items.Length; index++)
            {
                var item = items[index];
                if (!item.StartsWith("--", StringComparison.Ordinal) || item.Length == 2)
                    throw new CommandLineException($"Unexpected argument '{item}'. Options must start with --.");
                var name = item[2..];
                if (!parsed.TryAdd(name, null))
                    throw new CommandLineException($"Option '--{name}' was specified more than once.");
                if (index + 1 < items.Length && !items[index + 1].StartsWith("--", StringComparison.Ordinal))
                    parsed[name] = items[++index];
            }
            return new CommandOptions(parsed);
        }

        public bool HasFlag(string name) => values.TryGetValue(name, out var value) && value is null;

        public string Required(string name) => Optional(name)
            ?? throw new CommandLineException($"Missing required option '--{name}'.");

        public string? Optional(string name)
        {
            if (!values.TryGetValue(name, out var value))
                return null;
            if (value is null)
                throw new CommandLineException($"Option '--{name}' requires a value.");
            return value;
        }

        public int PositiveInt(string name, int defaultValue)
        {
            var value = Optional(name);
            if (value is null)
                return defaultValue;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result <= 0)
                throw new CommandLineException($"Option '--{name}' must be a positive integer.");
            return result;
        }
    }

    private sealed class CommandLineException(string message) : Exception(message);
}
