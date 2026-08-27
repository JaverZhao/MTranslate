using System.Collections.Concurrent;
using MTranslate.Core;

namespace MTranslate.Core.Tests;

public sealed class TranslationJobQueueTests
{
    [Fact]
    public async Task Queue_RunsHigherPriorityJobFirst()
    {
        await using var queue = new TranslationJobQueue();
        await queue.PauseAndDrainAsync();
        var order = new ConcurrentQueue<string>();
        var low = queue.EnqueueAsync(TranslationJobSource.File, TranslationJobPriority.Low,
            _ => { order.Enqueue("low"); return Task.FromResult(1); });
        var high = queue.EnqueueAsync(TranslationJobSource.OcrApi, TranslationJobPriority.High,
            _ => { order.Enqueue("high"); return Task.FromResult(2); });

        queue.Resume();
        await Task.WhenAll(low, high);

        Assert.Equal(["high", "low"], order);
    }

    [Fact]
    public async Task PauseAndDrain_WaitsForActiveJobAndBlocksPendingJob()
    {
        await using var queue = new TranslationJobQueue();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = queue.EnqueueAsync(TranslationJobSource.DesktopText, TranslationJobPriority.Normal, async token =>
        {
            started.SetResult();
            await release.Task.WaitAsync(token);
            return 1;
        });
        await started.Task;
        var secondStarted = false;
        var second = queue.EnqueueAsync(TranslationJobSource.DesktopText, TranslationJobPriority.Normal,
            _ => { secondStarted = true; return Task.FromResult(2); });

        var drained = queue.PauseAndDrainAsync();
        Assert.False(drained.IsCompleted);
        release.SetResult();
        await drained;
        Assert.False(secondStarted);

        queue.Resume();
        Assert.Equal([1, 2], await Task.WhenAll(first, second));
    }

    [Fact]
    public async Task Enqueue_PropagatesRequestCancellation()
    {
        await using var queue = new TranslationJobQueue();
        await queue.PauseAndDrainAsync();
        using var cancellation = new CancellationTokenSource();
        var task = queue.EnqueueAsync(TranslationJobSource.Api, TranslationJobPriority.High,
            _ => Task.FromResult(1), cancellation.Token);
        cancellation.Cancel();
        queue.Resume();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
