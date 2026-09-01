using System.Diagnostics;
using System.Net.Sockets;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using MTranslate.Core;

namespace MTranslate.Infrastructure;

public sealed record InferenceRuntimeConfiguration(
    string ExecutablePath,
    string ModelPath,
    string Host = "127.0.0.1",
    int Port = 17892,
    int ContextSize = 8192,
    int ParallelSlots = 2,
    int GpuLayers = 0,
    TimeSpan? StartupTimeout = null,
    string? ApiKey = null)
{
    public TimeSpan EffectiveStartupTimeout => StartupTimeout ?? TimeSpan.FromMinutes(3);

    public void Validate()
    {
        if (!File.Exists(ExecutablePath))
            throw new FileNotFoundException("llama-server executable was not found.", ExecutablePath);
        if (!File.Exists(ModelPath))
            throw new FileNotFoundException("GGUF model was not found.", ModelPath);
        if (Host is not "127.0.0.1" and not "localhost")
            throw new ArgumentException("The internal inference server must bind to loopback.", nameof(Host));
        if (Port is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(Port));
        if (ContextSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(ContextSize));
        if (ParallelSlots <= 0)
            throw new ArgumentOutOfRangeException(nameof(ParallelSlots));
        if (GpuLayers < 0)
            throw new ArgumentOutOfRangeException(nameof(GpuLayers));
    }
}

public sealed class LlamaServerRuntime : IInferenceRuntime
{
    private readonly InferenceRuntimeConfiguration configuration;
    private readonly HttpClient healthClient;
    private Process? process;
    private Task? outputPump;
    private Task? errorPump;

    public LlamaServerRuntime(InferenceRuntimeConfiguration configuration)
    {
        this.configuration = configuration;
        ApiKey = string.IsNullOrWhiteSpace(configuration.ApiKey)
            ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
            : configuration.ApiKey;
        healthClient = new HttpClient
        {
            BaseAddress = new Uri($"http://{configuration.Host}:{configuration.Port}/"),
            Timeout = TimeSpan.FromSeconds(2)
        };
        healthClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
    }

    public bool IsRunning => process is { HasExited: false };
    public string ApiKey { get; }
    public event EventHandler<RuntimeExitedEventArgs>? Exited;

    Task IInferenceRuntime.StartAsync(CancellationToken cancellationToken) => StartAsync(cancellationToken: cancellationToken);

    public async Task StartAsync(
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            throw new InvalidOperationException("llama-server is already running.");

        configuration.Validate();
        await EnsurePortAvailableAsync(configuration.Host, configuration.Port, cancellationToken).ConfigureAwait(false);

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(configuration.ExecutablePath),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add(Path.GetFullPath(configuration.ModelPath));
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add(configuration.Host);
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(configuration.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(configuration.ContextSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-np");
        startInfo.ArgumentList.Add(configuration.ParallelSlots.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--n-gpu-layers");
        startInfo.ArgumentList.Add(configuration.GpuLayers.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--api-key");
        startInfo.ArgumentList.Add(ApiKey);

        process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Exited += OnProcessExited;
        if (!process.Start())
            throw new InvalidOperationException("Failed to start llama-server.");

        outputPump = PumpAsync(process.StandardOutput, log, CancellationToken.None);
        errorPump = PumpAsync(process.StandardError, log, CancellationToken.None);

        using var timeout = new CancellationTokenSource(configuration.EffectiveStartupTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await WaitUntilHealthyAsync(linked.Token).ConfigureAwait(false);
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            if (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                throw new TimeoutException($"llama-server did not become healthy within {configuration.EffectiveStartupTimeout}.");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var current = process;
        if (current is null)
            return;

        if (!current.HasExited)
        {
            current.Kill(entireProcessTree: true);
            await current.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (outputPump is not null)
            await outputPump.ConfigureAwait(false);
        if (errorPump is not null)
            await errorPump.ConfigureAwait(false);
        current.Dispose();
        process = null;
        outputPump = null;
        errorPump = null;
    }

    private void OnProcessExited(object? sender, EventArgs eventArgs)
    {
        if (sender is Process exitedProcess)
            Exited?.Invoke(this, new RuntimeExitedEventArgs(exitedProcess.ExitCode));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        healthClient.Dispose();
    }

    private async Task WaitUntilHealthyAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process is { HasExited: true })
                throw new InvalidOperationException($"llama-server exited during startup with code {process.ExitCode}.");

            try
            {
                using var response = await healthClient.GetAsync("health", cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task PumpAsync(StreamReader reader, Action<string>? log, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            log?.Invoke(line);
    }

    private static async Task EnsurePortAvailableAsync(string host, int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return;
        }

        throw new IOException($"Port {port} on {host} is already in use.");
    }
}
