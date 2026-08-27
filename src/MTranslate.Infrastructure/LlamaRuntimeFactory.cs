using MTranslate.Core;

namespace MTranslate.Infrastructure;

public sealed class LlamaRuntimeFactory(
    Func<RuntimeModel, InferenceRuntimeConfiguration> configurationFactory,
    Action<string>? log = null) : IInferenceRuntimeFactory
{
    public IInferenceRuntime Create(RuntimeModel model)
    {
        var runtime = new LlamaServerRuntime(configurationFactory(model));
        return log is null ? runtime : new LoggingRuntime(runtime, log);
    }

    private sealed class LoggingRuntime(LlamaServerRuntime runtime, Action<string> log) : IInferenceRuntime
    {
        public bool IsRunning => runtime.IsRunning;
        public event EventHandler<RuntimeExitedEventArgs>? Exited
        {
            add => runtime.Exited += value;
            remove => runtime.Exited -= value;
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => runtime.StartAsync(log, cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken = default) => runtime.StopAsync(cancellationToken);
        public ValueTask DisposeAsync() => runtime.DisposeAsync();
    }
}
