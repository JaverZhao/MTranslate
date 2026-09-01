using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using MTranslate.Api;
using MTranslate.Core;
using MTranslate.Infrastructure;
using MTranslate.DocumentFormats;

namespace MTranslate.Desktop.Services;

public sealed class DesktopTranslationCoordinator : ITranslationCoordinator, ILocalApiTranslationBackend, IDisposable
{
    public const string FastModelId = "hy-mt2-1.8b-2bit";
    public const string StandardModelId = "hy-mt2-1.8b-q4";
    private const string FastModelFile = "Hy-MT2-1.8B-2Bit.gguf";
    private const string FastModelHash = "dcc33bbae9b28d923c8c76a64f6157840841d26f8774f3dfd770d5fabeeb1cd7";
    private static readonly Uri FastModelUri = new("https://huggingface.co/tencent/Hy-MT2-1.8B-2Bit-GGUF/resolve/main/Hy-MT2-1.8B-2Bit.gguf");
    private const string StandardModelFile = "Hy-MT2-1.8B-Q4_K_M.gguf";
    private const string StandardModelHash = "dc5f44fcf1fa496ee7ad725982c0c8c553a4de00259b53af84c4b89fb0c06699";
    private static readonly Uri StandardModelUri = new("https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF/resolve/main/Hy-MT2-1.8B-Q4_K_M.gguf");
    private readonly TranslationJobQueue queue = new(2);
    private readonly SqliteTranslationCache cache;
    private readonly SqliteTranslationHistoryStore history;
    private readonly DesktopRuntimeFactory runtimeFactory;
    private readonly RuntimeManager runtimeManager;
    private readonly ModelManager modelManager;
    private readonly DesktopPaths paths;
    private readonly ILanguageDetector languageDetector = new HeuristicLanguageDetector();
    private HttpClient? httpClient;
    private ITranslationClient? translationClient;
    private TranslationService? translationService;
    private string preferredModelId = StandardModelId;
    private InferenceAccelerationMode accelerationMode = InferenceAccelerationMode.Automatic;
    private bool accelerationDirty;
    private bool disposed;

    public DesktopTranslationCoordinator()
    {
        paths = DesktopPaths.Discover();
        cache = new SqliteTranslationCache(Path.Combine(paths.DataDirectory, "database", "app.db"));
        history = new SqliteTranslationHistoryStore(Path.Combine(paths.DataDirectory, "database", "app.db"));
        runtimeFactory = new DesktopRuntimeFactory(
            paths.StandardRuntimeExecutable,
            paths.StandardGpuRuntimeExecutable,
            paths.FastRuntimeExecutable);
        runtimeManager = new RuntimeManager(runtimeFactory);
        modelManager = new ModelManager(runtimeManager, queue);
        modelManager.Register(new ModelDefinition(
            FastModelId,
            "极速",
            paths.FastModel,
            FastModelHash));
        modelManager.Register(new ModelDefinition(
            StandardModelId,
            "标准",
            paths.StandardModel,
            StandardModelHash));
    }

    public bool CacheEnabled { get => cache.Enabled; set => cache.Enabled = value; }
    public bool HistoryEnabled { get; set; } = true;
    public InferenceAccelerationMode AccelerationMode
    {
        get => accelerationMode;
        set
        {
            if (accelerationMode == value)
                return;
            accelerationMode = value;
            runtimeFactory.Mode = value;
            accelerationDirty = true;
        }
    }
    public string AccelerationStatus => runtimeManager.Status == RuntimeStatus.Ready && !accelerationDirty
        ? runtimeFactory.LastBackend
        : runtimeFactory.DescribeBackend(preferredModelId);
    public string ModelStatus => FormatModelStatus(preferredModelId);
    public IReadOnlyList<DesktopModelInfo> ModelInfos =>
    [
        CreateModelInfo(FastModelId, "Hy-MT2 1.8B 极速", "Q2_0C · 2-Bit", 600_534_880, runtimeFactory.IsRuntimeAvailable(FastModelId)),
        CreateModelInfo(StandardModelId, "Hy-MT2 1.8B 标准", "Q4_K_M", 1_133_080_448, runtimeFactory.IsRuntimeAvailable(StandardModelId))
    ];
    public bool ModelLoaded => modelManager.ActiveModelId is { } activeId
        && modelManager.GetState(activeId) == ModelState.Ready;
    public string ActiveModelId => modelManager.ActiveModelId ?? preferredModelId;
    public IReadOnlyList<ApiModelDescriptor> Models => ModelInfos
        .Select(model => new ApiModelDescriptor(model.Id, model.DisplayName, model.IsInstalled && model.RuntimeAvailable))
        .ToArray();

    public Task DownloadModelAsync(
        string modelId,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var source = modelId switch
        {
            FastModelId => FastModelUri,
            StandardModelId => StandardModelUri,
            _ => throw new KeyNotFoundException($"未知模型：{modelId}")
        };
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        return DownloadAndDisposeAsync(modelId, source, client, progress, cancellationToken);
    }

    public async Task SelectModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (modelId == FastModelId && accelerationMode == InferenceAccelerationMode.Gpu)
            throw new InvalidOperationException("极速 Q2_0C 模型当前只支持 CPU 推理；请切换到标准 Q4 模型后使用 GPU。");
        if (!runtimeFactory.IsRuntimeAvailable(modelId))
            throw new InvalidOperationException(modelId == FastModelId
                ? "当前平台尚未安装 Q2_0C 兼容运行时。"
                : accelerationMode == InferenceAccelerationMode.Gpu
                    ? "当前平台尚未安装 GPU 推理运行时。"
                    : "当前平台尚未安装标准 llama.cpp 运行时。");
        await modelManager.SwitchAsync(modelId, cancellationToken, forceReload: accelerationDirty).ConfigureAwait(false);
        preferredModelId = modelId;
        accelerationDirty = false;
        CreateTranslationService();
    }

    private string FormatModelStatus(string modelId)
    {
        var prefix = modelId == FastModelId ? "极速模型" : "标准模型";
        var state = modelManager.GetState(modelId) switch
        {
            ModelState.Ready => "已就绪",
            ModelState.Installed => "已安装",
            ModelState.NotInstalled => "未安装",
            ModelState.Downloading => "正在下载",
            ModelState.Loading => "正在加载",
            ModelState.Unloading => "正在切换",
            ModelState.DownloadFailed => "下载失败",
            ModelState.ChecksumFailed => "校验失败",
            ModelState.LoadFailed => "加载失败",
            ModelState.RuntimeCrashed => "翻译引擎正在恢复",
            _ => "不可用"
        };
        var status = prefix + state;
        if (modelManager.GetState(modelId) == ModelState.Ready)
            status += $" · {runtimeFactory.LastBackend}";
        return status;
    }

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
            ModelProfile: CurrentModelProfile("desktop/prompt-v2-language-detection"),
            Source: TranslationJobSource.DesktopText,
            Priority: TranslationJobPriority.Normal), cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        var response = new DesktopTranslationResponse(result.Text, stopwatch.Elapsed, result.CacheHits, result.ChunkCount);
        if (HistoryEnabled)
        {
            await history.AddAsync(new TranslationHistoryEntry(
                Guid.NewGuid(),
                text,
                result.Text,
                effectiveSourceLanguage,
                targetLanguage,
                ActiveModelId,
                DateTimeOffset.UtcNow,
                stopwatch.Elapsed), cancellationToken).ConfigureAwait(false);
        }
        return response;
    }

    public Task<IReadOnlyList<TranslationHistoryEntry>> SearchHistoryAsync(
        string? query = null,
        CancellationToken cancellationToken = default) =>
        history.SearchAsync(query, cancellationToken: cancellationToken);

    public Task<bool> DeleteHistoryAsync(Guid id, CancellationToken cancellationToken = default) =>
        history.DeleteAsync(id, cancellationToken);

    public Task ClearHistoryAsync(CancellationToken cancellationToken = default) =>
        history.ClearAsync(cancellationToken);

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
            JobId: jobId,
            ModelProfile: CurrentModelProfile("document/prompt-v2-language-detection")), progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApiTranslationBackendResult> TranslateAsync(
        ApiTranslationCommand command,
        CancellationToken cancellationToken = default)
    {
        await EnsureApiModeAsync(command.Mode, cancellationToken).ConfigureAwait(false);
        var sourceLanguage = DetectSourceLanguage(command.Text, command.SourceLanguage);
        var stopwatch = Stopwatch.StartNew();
        var translated = new StringBuilder();
        var chunkCount = 0;
        var cacheHits = 0;
        string? previousSource = command.Context;
        foreach (var line in SplitLines(command.Text, command.PreserveLineBreaks))
        {
            if (line.Content.Length == 0 || string.IsNullOrWhiteSpace(line.Content))
            {
                translated.Append(line.Content).Append(line.Separator);
                continue;
            }
            var (leading, text, trailing) = SeparateWhitespace(line.Content);
            var result = await translationService!.TranslateAsync(new TranslationServiceRequest(
                text,
                command.TargetLanguage,
                sourceLanguage,
                ModelProfile: CurrentModelProfile("api-v1"),
                Source: TranslationJobSource.Api,
                Priority: TranslationJobPriority.Normal,
                Context: previousSource,
                UseCache: command.UseCache), cancellationToken).ConfigureAwait(false);
            translated.Append(leading).Append(result.Text.Trim()).Append(trailing).Append(line.Separator);
            chunkCount += result.ChunkCount;
            cacheHits += result.CacheHits;
            previousSource = text;
        }
        stopwatch.Stop();
        return new ApiTranslationBackendResult(
            translated.ToString(),
            sourceLanguage,
            ActiveModelId,
            chunkCount > 0 && cacheHits == chunkCount,
            stopwatch.Elapsed);
    }

    public async IAsyncEnumerable<string> TranslateStreamingAsync(
        ApiTranslationCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureApiModeAsync(command.Mode, cancellationToken).ConfigureAwait(false);
        var sourceLanguage = DetectSourceLanguage(command.Text, command.SourceLanguage);
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var producer = queue.EnqueueAsync(
            TranslationJobSource.Api,
            TranslationJobPriority.Normal,
            async token =>
            {
                try
                {
                    string? previousSource = command.Context;
                    foreach (var line in SplitLines(command.Text, command.PreserveLineBreaks))
                    {
                        if (line.Content.Length == 0 || string.IsNullOrWhiteSpace(line.Content))
                        {
                            await channel.Writer.WriteAsync(line.Content + line.Separator, token).ConfigureAwait(false);
                            continue;
                        }
                        var (leading, text, trailing) = SeparateWhitespace(line.Content);
                        if (leading.Length > 0)
                            await channel.Writer.WriteAsync(leading, token).ConfigureAwait(false);
                        await foreach (var chunk in translationClient!.TranslateStreamingAsync(new TranslationRequest(
                                           text,
                                           command.TargetLanguage,
                                           sourceLanguage,
                                           previousSource), token).ConfigureAwait(false))
                            await channel.Writer.WriteAsync(chunk.Text, token).ConfigureAwait(false);
                        if (trailing.Length > 0 || line.Separator.Length > 0)
                            await channel.Writer.WriteAsync(trailing + line.Separator, token).ConfigureAwait(false);
                        previousSource = text;
                    }
                    channel.Writer.TryComplete();
                    return true;
                }
                catch (Exception exception)
                {
                    channel.Writer.TryComplete(exception);
                    throw;
                }
            }, cancellationToken);

        await foreach (var delta in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return delta;
        await producer.ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        httpClient?.Dispose();
        cache.DisposeAsync().AsTask().GetAwaiter().GetResult();
        history.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
        translationClient = new LlamaServerTranslationClient(httpClient, new TranslationPromptBuilder());
        translationService = new TranslationService(translationClient, new ChunkManager(), cache, queue);
    }

    private async Task EnsureTranslationServiceAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (modelManager.GetState(preferredModelId) == ModelState.NotInstalled)
            throw new InvalidOperationException("当前模型尚未安装。请前往“模型”页面完成下载后再翻译。");
        if (!runtimeFactory.IsRuntimeAvailable(preferredModelId))
            throw new InvalidOperationException("当前模型所需的推理运行时尚未安装。");
        if (runtimeManager.Status == RuntimeStatus.Ready
            && modelManager.ActiveModelId == preferredModelId
            && !accelerationDirty
            && translationService is not null)
            return;
        await modelManager.SwitchAsync(preferredModelId, cancellationToken, forceReload: accelerationDirty).ConfigureAwait(false);
        accelerationDirty = false;
        CreateTranslationService();
    }

    private async Task EnsureApiModeAsync(string mode, CancellationToken cancellationToken)
    {
        var modelId = mode.Equals("fast", StringComparison.OrdinalIgnoreCase)
            ? FastModelId
            : StandardModelId;
        if (preferredModelId != modelId
            || modelManager.ActiveModelId != modelId
            || runtimeManager.Status != RuntimeStatus.Ready)
            await SelectModelAsync(modelId, cancellationToken).ConfigureAwait(false);
        else
            await EnsureTranslationServiceAsync(cancellationToken).ConfigureAwait(false);
    }

    private string CurrentModelProfile(string suffix) =>
        $"{(preferredModelId == FastModelId ? "fast-q2-0c" : "standard-q4-k-m")}/{suffix}";

    private string DetectSourceLanguage(string text, string sourceLanguage)
    {
        var detected = sourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? languageDetector.Detect(text)?.LanguageCode
            : sourceLanguage;
        return detected ?? throw new InvalidOperationException("无法可靠识别源语言，请手动指定 sourceLanguage。");
    }

    private static IEnumerable<ApiTextLine> SplitLines(string text, bool preserveLineBreaks)
    {
        if (!preserveLineBreaks)
        {
            yield return new ApiTextLine(text, string.Empty);
            yield break;
        }
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('\r' or '\n'))
                continue;
            var separatorLength = text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
            yield return new ApiTextLine(text[start..index], text.Substring(index, separatorLength));
            index += separatorLength - 1;
            start = index + 1;
        }
        if (start < text.Length)
            yield return new ApiTextLine(text[start..], string.Empty);
    }

    private static (string Leading, string Text, string Trailing) SeparateWhitespace(string value)
    {
        var start = 0;
        while (start < value.Length && char.IsWhiteSpace(value[start])) start++;
        var end = value.Length;
        while (end > start && char.IsWhiteSpace(value[end - 1])) end--;
        return (value[..start], value[start..end], value[end..]);
    }

    private DesktopModelInfo CreateModelInfo(
        string id,
        string displayName,
        string quantization,
        long sizeBytes,
        bool runtimeAvailable)
    {
        var state = modelManager.GetState(id);
        var installed = state is ModelState.Installed or ModelState.Loading or ModelState.Ready
            or ModelState.Unloading or ModelState.LoadFailed or ModelState.RuntimeCrashed;
        var status = FormatModelStatus(id);
        if (installed && !runtimeAvailable)
            status += id == FastModelId && accelerationMode == InferenceAccelerationMode.Gpu
                ? " · GPU 模式不支持 Q2_0C"
                : " · 缺少兼容运行时";
        return new DesktopModelInfo(
            id,
            displayName,
            quantization,
            sizeBytes,
            status,
            installed,
            modelManager.ActiveModelId == id && state == ModelState.Ready,
            runtimeAvailable);
    }

    private async Task DownloadAndDisposeAsync(
        string modelId,
        Uri source,
        HttpClient client,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using (client)
            await modelManager.DownloadAsync(
                modelId,
                source,
                new ResumableModelDownloader(client),
                progress,
                cancellationToken).ConfigureAwait(false);
    }

    private sealed record ApiTextLine(string Content, string Separator);

    private sealed class DesktopRuntimeFactory(
        string standardExecutablePath,
        string standardGpuExecutablePath,
        string fastExecutablePath) : IInferenceRuntimeFactory
    {
        public string ApiKey { get; private set; } = string.Empty;
        public InferenceAccelerationMode Mode { get; set; } = InferenceAccelerationMode.Automatic;
        public string LastBackend { get; private set; } = "尚未启动";

        public bool IsRuntimeAvailable(string modelId)
        {
            if (modelId == FastModelId)
                return Mode != InferenceAccelerationMode.Gpu && File.Exists(fastExecutablePath);
            return Mode switch
            {
                InferenceAccelerationMode.Cpu => File.Exists(standardExecutablePath),
                InferenceAccelerationMode.Gpu => File.Exists(standardGpuExecutablePath),
                _ => File.Exists(standardGpuExecutablePath) || File.Exists(standardExecutablePath)
            };
        }

        public string DescribeBackend(string modelId)
        {
            if (modelId == FastModelId)
                return Mode == InferenceAccelerationMode.Gpu ? "GPU 不支持 Q2_0C" : "CPU · Q2_0C 兼容内核";
            var useGpu = Mode == InferenceAccelerationMode.Gpu
                || Mode == InferenceAccelerationMode.Automatic && File.Exists(standardGpuExecutablePath);
            return useGpu ? "GPU · Vulkan" : "CPU";
        }

        public IInferenceRuntime Create(RuntimeModel model)
        {
            var useGpu = model.Id != FastModelId
                && (Mode == InferenceAccelerationMode.Gpu
                    || Mode == InferenceAccelerationMode.Automatic && File.Exists(standardGpuExecutablePath));
            var executablePath = model.Id == FastModelId
                ? fastExecutablePath
                : useGpu ? standardGpuExecutablePath : standardExecutablePath;
            if (!File.Exists(executablePath))
                throw new FileNotFoundException("当前平台的 llama-server runtime 尚未安装。", executablePath);
            var runtime = new LlamaServerRuntime(new InferenceRuntimeConfiguration(
                executablePath,
                model.ModelPath,
                ContextSize: 8192,
                ParallelSlots: 2,
                GpuLayers: useGpu ? 999 : 0));
            ApiKey = runtime.ApiKey;
            LastBackend = useGpu ? "GPU · Vulkan" : model.Id == FastModelId ? "CPU · Q2_0C" : "CPU";
            return runtime;
        }
    }

    private sealed record DesktopPaths(
        string DataDirectory,
        string StandardRuntimeExecutable,
        string StandardGpuRuntimeExecutable,
        string FastRuntimeExecutable,
        string StandardModel,
        string FastModel)
    {
        public static DesktopPaths Discover()
        {
            var dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MTranslate");
            var workspace = FindWorkspaceRoot();
            var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
            var runtimeRelative = OperatingSystem.IsWindows()
                ? Path.Combine("runtime", architecture == System.Runtime.InteropServices.Architecture.Arm64 ? "win-arm64" : "win-x64", "llama-server.exe")
                : Path.Combine("runtime", architecture == System.Runtime.InteropServices.Architecture.Arm64 ? "osx-arm64" : "osx-x64", "llama-server");
            var fastRuntimeRelative = OperatingSystem.IsWindows()
                ? Path.Combine("runtime", architecture == System.Runtime.InteropServices.Architecture.Arm64 ? "q2c-win-arm64" : "q2c-win-x64", "llama-server.exe")
                : Path.Combine("runtime", architecture == System.Runtime.InteropServices.Architecture.Arm64 ? "q2c-osx-arm64" : "q2c-osx-x64", "llama-server");
            var gpuRuntimeRelative = OperatingSystem.IsWindows() && architecture == System.Runtime.InteropServices.Architecture.X64
                ? Path.Combine("runtime", "win-vulkan-x64", "llama-server.exe")
                : OperatingSystem.IsMacOS()
                    ? runtimeRelative
                    : Path.Combine("runtime", "gpu-unavailable", OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server");
            var installedRuntime = Path.Combine(dataDirectory, runtimeRelative);
            var developmentRuntime = Path.Combine(workspace, runtimeRelative);
            var installedGpuRuntime = Path.Combine(dataDirectory, gpuRuntimeRelative);
            var developmentGpuRuntime = Path.Combine(workspace, gpuRuntimeRelative);
            var installedFastRuntime = Path.Combine(dataDirectory, fastRuntimeRelative);
            var developmentFastRuntime = Path.Combine(workspace, fastRuntimeRelative);
            var installedModel = Path.Combine(dataDirectory, "models", StandardModelFile);
            var developmentModel = Path.Combine(workspace, "models", StandardModelFile);
            var installedFastModel = Path.Combine(dataDirectory, "models", FastModelFile);
            var developmentFastModel = Path.Combine(workspace, "models", FastModelFile);
            return new DesktopPaths(
                dataDirectory,
                File.Exists(installedRuntime) ? installedRuntime : developmentRuntime,
                File.Exists(installedGpuRuntime) ? installedGpuRuntime : developmentGpuRuntime,
                File.Exists(installedFastRuntime) ? installedFastRuntime : developmentFastRuntime,
                File.Exists(installedModel) ? installedModel : File.Exists(developmentModel) ? developmentModel : installedModel,
                File.Exists(installedFastModel) ? installedFastModel : File.Exists(developmentFastModel) ? developmentFastModel : installedFastModel);
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
