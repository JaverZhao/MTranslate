namespace MTranslate.Core;

public enum TranslationJobPriority { High, Normal, Low }
public enum TranslationJobSource { DesktopText, File, Browser, OcrApi, Api, Other }

public sealed record TranslationJob(
    Guid Id,
    TranslationJobSource Source,
    TranslationJobPriority Priority,
    DateTimeOffset CreatedAt);

public interface ITranslationJobQueue
{
    bool IsPaused { get; }
    int PendingCount { get; }

    Task<T> EnqueueAsync<T>(
        TranslationJobSource source,
        TranslationJobPriority priority,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);

    Task PauseAndDrainAsync(CancellationToken cancellationToken = default);
    void Resume();
}

public sealed class TranslationJobQueue : ITranslationJobQueue, IAsyncDisposable
{
    private readonly object sync = new();
    private readonly Queue<IQueuedJob>[] queues = [new(), new(), new()];
    private readonly SemaphoreSlim available = new(0);
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task[] workers;
    private TaskCompletionSource drained = CompletedSource();
    private TaskCompletionSource resumed = CompletedSource();
    private bool paused;
    private bool disposed;
    private int activeCount;
    private int pendingCount;

    public TranslationJobQueue(int parallelSlots = 1)
    {
        if (parallelSlots is < 1 or > 2)
            throw new ArgumentOutOfRangeException(nameof(parallelSlots), "Initial inference parallelism must be one or two.");
        workers = Enumerable.Range(0, parallelSlots).Select(_ => WorkerAsync()).ToArray();
    }

    public bool IsPaused { get { lock (sync) return paused; } }
    public int PendingCount { get { lock (sync) return pendingCount; } }

    public Task<T> EnqueueAsync<T>(
        TranslationJobSource source,
        TranslationJobPriority priority,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var queued = new QueuedJob<T>(
            new TranslationJob(Guid.NewGuid(), source, priority, DateTimeOffset.UtcNow),
            operation,
            cancellationToken);

        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            queues[(int)priority].Enqueue(queued);
            pendingCount++;
        }

        available.Release();
        return queued.Task;
    }

    public Task PauseAndDrainAsync(CancellationToken cancellationToken = default)
    {
        Task wait;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!paused)
            {
                paused = true;
                resumed = NewSource();
            }

            if (activeCount == 0)
                return Task.CompletedTask;
            wait = drained.Task;
        }

        return wait.WaitAsync(cancellationToken);
    }

    public void Resume()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!paused)
                return;
            paused = false;
            resumed.TrySetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (sync)
        {
            if (disposed)
                return;
            disposed = true;
            shutdown.Cancel();
            resumed.TrySetResult();
            foreach (var queue in queues)
            {
                while (queue.TryDequeue(out var job))
                    job.Cancel();
            }
            pendingCount = 0;
        }

        available.Release(workers.Length);
        await Task.WhenAll(workers).ConfigureAwait(false);
        available.Dispose();
        shutdown.Dispose();
    }

    private async Task WorkerAsync()
    {
        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                await available.WaitAsync(shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            IQueuedJob? job;
            Task? resumeWait = null;
            lock (sync)
            {
                if (paused)
                {
                    available.Release();
                    resumeWait = resumed.Task;
                    job = null;
                }
                else
                {
                    job = DequeueUnsafe();
                    if (job is not null)
                    {
                        activeCount++;
                        if (activeCount == 1)
                            drained = NewSource();
                    }
                }
            }

            if (resumeWait is not null)
            {
                try { await resumeWait.WaitAsync(shutdown.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                continue;
            }
            if (job is null)
                continue;

            await job.ExecuteAsync(shutdown.Token).ConfigureAwait(false);
            lock (sync)
            {
                activeCount--;
                if (activeCount == 0)
                    drained.TrySetResult();
            }
        }
    }

    private IQueuedJob? DequeueUnsafe()
    {
        foreach (var queue in queues)
        {
            if (queue.TryDequeue(out var job))
            {
                pendingCount--;
                return job;
            }
        }
        return null;
    }

    private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource CompletedSource()
    {
        var source = NewSource();
        source.SetResult();
        return source;
    }

    private interface IQueuedJob
    {
        Task ExecuteAsync(CancellationToken shutdownToken);
        void Cancel();
    }

    private sealed class QueuedJob<T>(
        TranslationJob job,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken requestToken) : IQueuedJob
    {
        private readonly TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TranslationJob Job { get; } = job;
        public Task<T> Task => completion.Task;

        public async Task ExecuteAsync(CancellationToken shutdownToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestToken, shutdownToken);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                completion.TrySetResult(await operation(linked.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                completion.TrySetCanceled(requestToken.IsCancellationRequested ? requestToken : shutdownToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        public void Cancel() => completion.TrySetCanceled();
    }
}
