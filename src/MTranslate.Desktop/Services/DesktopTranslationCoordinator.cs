using System.Diagnostics;
using System.Net.Http.Headers;
using MTranslate.Core;
using MTranslate.Infrastructure;
using MTranslate.DocumentFormats;

namespace MTranslate.Desktop.Services;

public sealed class DesktopTranslationCoordinator : ITranslationCoordinator, IDisposable
{
    private const string StandardModelId = "hy-mt2-1.8b-standard";
    private const string StandardModelFile = "Hy-MT2-1.8B-Q4_K_M.gguf";
    private const string StandardModelHash = "dc5f44fcf1fa496ee7ad725982c0c8c553a4de00259b53af84c4b89fb0c06699";
    private readonly TranslationJobQueue queue = new(2);
    private readonly SqliteTranslationCache cache;
    private readonly DesktopRuntimeFactory runtimeFactory;
    private readonly RuntimeManager runtimeManager;
    private readonly ModelManager modelManager;
    private readonly ILanguageDetector languageDetector = new HeuristicLanguageDetector();
    private HttpClient? httpClient;
    private TranslationService? translationService;
    private bool disposed;

    public DesktopTranslationCoordinator()
    {
        var paths = DesktopPaths.Discover();
        cache = new SqliteTranslationCache(Path.Combine(paths.DataDirectory, "database", "app.db"));
        runtimeFactory = new DesktopRuntimeFactory(paths.RuntimeExecutable);
        runtimeManager = new RuntimeManager(runtimeFactory);
        modelManager = new ModelManager(runtimeManager, queue);
        modelManager.Register(new ModelDefinition(
            StandardModelId,
            "标准",
            paths.StandardModel,
            StandardModelHash));
    }

    public bool CacheEnabled { get => cache.Enabled; set => cache.Enabled = value; }
    public string ModelStatus => modelManager.GetState(StandardModelId) switch
    {
        ModelState.Ready => "标准模型已就绪",
        ModelState.Installed => "标准模型已安装",
        ModelState.NotInstalled => "标准模型未安装",
        ModelState.Loading => "标准模型正在加载",
        ModelState.ChecksumFailed => "标准模型校验失败",
        ModelState.LoadFailed => "标准模型加载失败",
        ModelState.RuntimeCrashed => "翻译引擎正在恢复",
        _ => "标准模型不可用"
    };

    public async Task<DesktopTranslationResponse> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await EnsureTranslationServiceAsync(cancellationToken).ConfigureAwait(false);

        var effectiveSourceLanguage = sourceLanguage == "auto"
            ? languageDetector.Detect(text)?.LanguageCode
            : sourceLanguage;
        if (effectiveSourceLanguage is null)
            throw new InvalidOperationException("无法可靠识别源语言，请手动选择源语言后重试。");

        var stopwatch = Stopwatch.StartNew();
        var result = await translationService!.TranslateAsync(new TranslationServiceRequest(
            text,
            targetLanguage,
            effectiveSourceLanguage,
            ModelProfile: "standard-q4-k-m/prompt-v2-language-detection",
            Source: TranslationJobSource.DesktopText,
            Priority: TranslationJobPriority.Normal), cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        return new DesktopTranslationResponse(result.Text, stopwatch.Elapsed, result.CacheHits, result.ChunkCount);
    }

    public async Task<DocumentTranslationResult> TranslateDocumentAsync(
        string inputPath,
        string outputPath,
        string sourceLanguage,
        string targetLanguage,
        SubtitleOutputMode subtitleOutput,
        Guid jobId,
        IProgress<DocumentTranslationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureTranslationServiceAsync(cancellationToken).ConfigureAwait(false);
        var checkpoints = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MTranslate",
            "temp",
            "document-checkpoints");
        var translator = new DocumentTranslator(
            translationService!,
            new DocumentParserRegistry(),
            new FileDocumentCheckpointStore(checkpoints),
            new HeuristicTokenEstimator());
        return await translator.TranslateAsync(new DocumentTranslationRequest(
            inputPath,
            outputPath,
            targetLanguage,
            sourceLanguage == "auto" ? null : sourceLanguage,
            SubtitleOutput: subtitleOutput,
            JobId: jobId), progress, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        httpClient?.Dispose();
        cache.DisposeAsync().AsTask().GetAwaiter().GetResult();
        runtimeManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
        queue.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void CreateTranslationService()
    {
        httpClient?.Dispose();
        httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:17892/"),
            Timeout = TimeSpan.FromMinutes(10)
        };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", runtimeFactory.ApiKey);
        var client = new LlamaServerTranslationClient(httpClient, new TranslationPromptBuilder());
        translationService = new TranslationService(client, new ChunkManager(), cache, queue);
    }

    private async Task EnsureTranslationServiceAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (modelManager.GetState(StandardModelId) == ModelState.NotInstalled)
            throw new InvalidOperationException("标准模型尚未安装。请前往“模型”页面完成下载后再翻译。");
        if (runtimeManager.Status == RuntimeStatus.Ready && translationService is not null)
            return;
        await modelManager.SwitchAsync(StandardModelId, cancellationToken).ConfigureAwait(false);
        CreateTranslationService();
    }

    private sealed class DesktopRuntimeFactory(string executablePath) : IInferenceRuntimeFactory
    {
        public string ApiKey { get; private set; } = string.Empty;

        public IInferenceRuntime Create(RuntimeModel model)
        {
            if (!File.Exists(executablePath))
                throw new FileNotFoundException("当前平台的 llama-server runtime 尚未安装。", executablePath);
            var runtime = new LlamaServerRuntime(new InferenceRuntimeConfiguration(
                executablePath,
                model.ModelPath,
                ContextSize: 8192,
                ParallelSlots: 2));
            ApiKey = runtime.ApiKey;
            return runtime;
        }
    }

    private sealed record DesktopPaths(string DataDirectory, string RuntimeExecutable, string StandardModel)
    {
        public static DesktopPaths Discover()
        {
            var dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MTranslate");
            var workspace = FindWorkspaceRoot();
            var runtimeRelative = OperatingSystem.IsWindows()
                ? Path.Combine("runtime", Environment.Is64BitProcess ? "win-x64" : "win-arm64", "llama-server.exe")
                : Path.Combine("runtime", System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "osx-arm64" : "osx-x64", "llama-server");
            var installedRuntime = Path.Combine(dataDirectory, runtimeRelative);
            var developmentRuntime = Path.Combine(workspace, runtimeRelative);
            var installedModel = Path.Combine(dataDirectory, "models", StandardModelFile);
            var developmentModel = Path.Combine(workspace, "models", StandardModelFile);
            return new DesktopPaths(
                dataDirectory,
                File.Exists(installedRuntime) ? installedRuntime : developmentRuntime,
                File.Exists(installedModel) ? installedModel : developmentModel);
        }

        private static string FindWorkspaceRoot()
        {
            var candidates = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
            foreach (var candidate in candidates)
            {
                var directory = new DirectoryInfo(candidate);
                while (directory is not null)
                {
                    if (File.Exists(Path.Combine(directory.FullName, "MTranslate.slnx")))
                        return directory.FullName;
                    directory = directory.Parent;
                }
            }
            return AppContext.BaseDirectory;
        }
    }
}
