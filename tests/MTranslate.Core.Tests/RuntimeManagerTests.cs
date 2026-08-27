using MTranslate.Core;

namespace MTranslate.Core.Tests;

public sealed class RuntimeManagerTests
{
    [Fact]
    public async Task LoadAsync_StopsOldRuntimeBeforeStartingNewRuntime()
    {
        var factory = new FakeFactory();
        await using var manager = new RuntimeManager(factory);

        await manager.LoadAsync(new RuntimeModel("fast", "fast.gguf"));
        await manager.LoadAsync(new RuntimeModel("standard", "standard.gguf"));

        Assert.False(factory.Instances[0].IsRunning);
        Assert.True(factory.Instances[1].IsRunning);
        Assert.Equal("standard", manager.CurrentModel?.Id);
        Assert.Equal(RuntimeStatus.Ready, manager.Status);
    }

    [Fact]
    public async Task UnexpectedExit_RestartsThenStopsAfterThirdCrash()
    {
        var factory = new FakeFactory();
        await using var manager = new RuntimeManager(factory);
        await manager.LoadAsync(new RuntimeModel("fast", "fast.gguf"));

        factory.Instances[0].Crash();
        await WaitForAsync(() => factory.Instances.Count == 2 && manager.Status == RuntimeStatus.Ready);
        factory.Instances[1].Crash();
        await WaitForAsync(() => factory.Instances.Count == 3 && manager.Status == RuntimeStatus.Ready);
        factory.Instances[2].Crash();
        await WaitForAsync(() => manager.Status == RuntimeStatus.Faulted);

        Assert.Equal(3, factory.Instances.Count);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class FakeFactory : IInferenceRuntimeFactory
    {
        public List<FakeRuntime> Instances { get; } = [];
        public IInferenceRuntime Create(RuntimeModel model)
        {
            var runtime = new FakeRuntime();
            Instances.Add(runtime);
            return runtime;
        }
    }

    private sealed class FakeRuntime : IInferenceRuntime
    {
        public bool IsRunning { get; private set; }
        public event EventHandler<RuntimeExitedEventArgs>? Exited;
        public Task StartAsync(CancellationToken cancellationToken = default) { IsRunning = true; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken = default) { IsRunning = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { IsRunning = false; return ValueTask.CompletedTask; }
        public void Crash()
        {
            IsRunning = false;
            Exited?.Invoke(this, new RuntimeExitedEventArgs(1));
        }
    }
}
