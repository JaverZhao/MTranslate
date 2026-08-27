namespace MTranslate.Core;

public sealed record RuntimeModel(string Id, string ModelPath);

public sealed class RuntimeExitedEventArgs(int exitCode) : EventArgs
{
    public int ExitCode { get; } = exitCode;
}

public interface IInferenceRuntime : IAsyncDisposable
{
    bool IsRunning { get; }
    event EventHandler<RuntimeExitedEventArgs>? Exited;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface IInferenceRuntimeFactory
{
    IInferenceRuntime Create(RuntimeModel model);
}

public enum RuntimeStatus { Stopped, Starting, Ready, Recovering, Faulted }

public sealed class RuntimeStatusChangedEventArgs(RuntimeStatus status, string? message = null) : EventArgs
{
    public RuntimeStatus Status { get; } = status;
    public string? Message { get; } = message;
}

public sealed class RuntimeManager(
    IInferenceRuntimeFactory factory,
    TimeProvider? timeProvider = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Queue<DateTimeOffset> crashes = new();
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;
    private IInferenceRuntime? runtime;
    private RuntimeModel? model;
    private bool expectedExit;
    private bool disposed;

    public RuntimeStatus Status { get; private set; } = RuntimeStatus.Stopped;
    public RuntimeModel? CurrentModel => model;
    public event EventHandler<RuntimeStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<RuntimeExitedEventArgs>? RuntimeInterrupted;

    public async Task LoadAsync(RuntimeModel nextModel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nextModel);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            await StopCurrentUnsafeAsync(cancellationToken).ConfigureAwait(false);
            model = nextModel;
            runtime = factory.Create(nextModel);
            runtime.Exited += OnRuntimeExited;
            SetStatus(RuntimeStatus.Starting);
            try
            {
                await runtime.StartAsync(cancellationToken).ConfigureAwait(false);
                crashes.Clear();
                SetStatus(RuntimeStatus.Ready);
            }
            catch
            {
                SetStatus(RuntimeStatus.Faulted, "Translation engine failed to start.");
                await DisposeRuntimeUnsafeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCurrentUnsafeAsync(cancellationToken).ConfigureAwait(false);
            model = null;
            SetStatus(RuntimeStatus.Stopped);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed)
                return;
            disposed = true;
            await StopCurrentUnsafeAsync(CancellationToken.None).ConfigureAwait(false);
            model = null;
            SetStatus(RuntimeStatus.Stopped);
        }
        finally
        {
            gate.Release();
            gate.Dispose();
        }
    }

    private void OnRuntimeExited(object? sender, RuntimeExitedEventArgs eventArgs)
    {
        if (expectedExit || disposed)
            return;
        RuntimeInterrupted?.Invoke(this, eventArgs);
        _ = RecoverAsync(sender as IInferenceRuntime, eventArgs);
    }

    private async Task RecoverAsync(IInferenceRuntime? failedRuntime, RuntimeExitedEventArgs eventArgs)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed || expectedExit || failedRuntime is null || !ReferenceEquals(runtime, failedRuntime) || model is null)
                return;

            RecordCrash();
            if (crashes.Count >= 3)
            {
                SetStatus(RuntimeStatus.Faulted, "Translation engine failed three times within five minutes.");
                await DisposeRuntimeUnsafeAsync().ConfigureAwait(false);
                return;
            }

            SetStatus(RuntimeStatus.Recovering, $"Runtime exited with code {eventArgs.ExitCode}; restarting once.");
            await DisposeRuntimeUnsafeAsync().ConfigureAwait(false);
            runtime = factory.Create(model);
            runtime.Exited += OnRuntimeExited;
            try
            {
                await runtime.StartAsync(CancellationToken.None).ConfigureAwait(false);
                SetStatus(RuntimeStatus.Ready);
            }
            catch (Exception exception)
            {
                RecordCrash();
                SetStatus(RuntimeStatus.Faulted, $"Translation engine restart failed: {exception.Message}");
                await DisposeRuntimeUnsafeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private void RecordCrash()
    {
        var now = timeProvider.GetUtcNow();
        crashes.Enqueue(now);
        while (crashes.TryPeek(out var first) && now - first > TimeSpan.FromMinutes(5))
            crashes.Dequeue();
    }

    private async Task StopCurrentUnsafeAsync(CancellationToken cancellationToken)
    {
        if (runtime is null)
            return;
        expectedExit = true;
        try
        {
            await runtime.StopAsync(cancellationToken).ConfigureAwait(false);
            await DisposeRuntimeUnsafeAsync().ConfigureAwait(false);
        }
        finally
        {
            expectedExit = false;
        }
    }

    private async Task DisposeRuntimeUnsafeAsync()
    {
        if (runtime is null)
            return;
        var current = runtime;
        runtime = null;
        current.Exited -= OnRuntimeExited;
        await current.DisposeAsync().ConfigureAwait(false);
    }

    private void SetStatus(RuntimeStatus status, string? message = null)
    {
        Status = status;
        StatusChanged?.Invoke(this, new RuntimeStatusChangedEventArgs(status, message));
    }
}
