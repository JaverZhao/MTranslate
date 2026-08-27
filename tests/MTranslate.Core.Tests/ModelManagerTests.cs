using System.Security.Cryptography;
using MTranslate.Core;

namespace MTranslate.Core.Tests;

public sealed class ModelManagerTests
{
    [Fact]
    public async Task SwitchAsync_VerifiesModelPausesQueueAndMarksReady()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "model-content");
            var checksum = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
            await using var queue = new TranslationJobQueue();
            var factory = new FakeFactory();
            await using var runtimeManager = new RuntimeManager(factory);
            var manager = new ModelManager(runtimeManager, queue);
            manager.Register(new ModelDefinition("fast", "Fast", path, checksum));

            await manager.SwitchAsync("fast");

            Assert.Equal("fast", manager.ActiveModelId);
            Assert.Equal(ModelState.Ready, manager.GetState("fast"));
            Assert.False(queue.IsPaused);
            Assert.Single(factory.Created);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task VerifyInstalledAsync_RejectsChecksumMismatch()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "wrong-content");
            await using var queue = new TranslationJobQueue();
            await using var runtimeManager = new RuntimeManager(new FakeFactory());
            var manager = new ModelManager(runtimeManager, queue);
            manager.Register(new ModelDefinition("fast", "Fast", path, new string('A', 64)));

            Assert.False(await manager.VerifyInstalledAsync("fast"));
            Assert.Equal(ModelState.ChecksumFailed, manager.GetState("fast"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class FakeFactory : IInferenceRuntimeFactory
    {
        public List<RuntimeModel> Created { get; } = [];
        public IInferenceRuntime Create(RuntimeModel model) { Created.Add(model); return new FakeRuntime(); }
    }

    private sealed class FakeRuntime : IInferenceRuntime
    {
        public bool IsRunning { get; private set; }
        public event EventHandler<RuntimeExitedEventArgs>? Exited { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) { IsRunning = true; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken = default) { IsRunning = false; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
