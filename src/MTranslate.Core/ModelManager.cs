using System.Security.Cryptography;

namespace MTranslate.Core;

public sealed record ModelDefinition(string Id, string DisplayName, string FilePath, string Sha256);

public enum ModelState
{
    NotInstalled,
    Downloading,
    Installed,
    Loading,
    Ready,
    Unloading,
    DownloadFailed,
    ChecksumFailed,
    LoadFailed,
    RuntimeCrashed
}

public sealed class ModelStateChangedEventArgs(ModelDefinition model, ModelState state) : EventArgs
{
    public ModelDefinition Model { get; } = model;
    public ModelState State { get; } = state;
}

public sealed class ModelManager
{
    private readonly RuntimeManager runtimeManager;
    private readonly ITranslationJobQueue jobQueue;
    private readonly Dictionary<string, ModelDefinition> models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModelState> states = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim switchGate = new(1, 1);

    public string? ActiveModelId { get; private set; }
    public event EventHandler<ModelStateChangedEventArgs>? StateChanged;

    public ModelManager(RuntimeManager runtimeManager, ITranslationJobQueue jobQueue)
    {
        this.runtimeManager = runtimeManager;
        this.jobQueue = jobQueue;
        runtimeManager.RuntimeInterrupted += OnRuntimeInterrupted;
        runtimeManager.StatusChanged += OnRuntimeStatusChanged;
    }

    public void Register(ModelDefinition model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (string.IsNullOrWhiteSpace(model.Id) || string.IsNullOrWhiteSpace(model.FilePath) || string.IsNullOrWhiteSpace(model.Sha256))
            throw new ArgumentException("Model id, file path, and checksum are required.", nameof(model));
        models.Add(model.Id, model);
        SetState(model, File.Exists(model.FilePath) ? ModelState.Installed : ModelState.NotInstalled);
    }

    public ModelState GetState(string modelId) => states.TryGetValue(modelId, out var state)
        ? state
        : throw new KeyNotFoundException($"Model '{modelId}' is not registered.");

    public async Task<bool> VerifyInstalledAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var model = GetModel(modelId);
        if (!File.Exists(model.FilePath))
        {
            SetState(model, ModelState.NotInstalled);
            return false;
        }

        await using var stream = File.OpenRead(model.FilePath);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        var valid = hash.Equals(model.Sha256, StringComparison.OrdinalIgnoreCase);
        SetState(model, valid ? ModelState.Installed : ModelState.ChecksumFailed);
        return valid;
    }

    public async Task SwitchAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var model = GetModel(modelId);
        await switchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ActiveModelId is not null && ActiveModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase)
                && runtimeManager.Status == RuntimeStatus.Ready)
                return;
            if (!await VerifyInstalledAsync(modelId, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException($"Model '{model.DisplayName}' is missing or failed checksum verification.");

            try
            {
                await jobQueue.PauseAndDrainAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                jobQueue.Resume();
                throw;
            }
            if (ActiveModelId is { } previousId && models.TryGetValue(previousId, out var previous))
                SetState(previous, ModelState.Unloading);

            SetState(model, ModelState.Loading);
            try
            {
                await runtimeManager.LoadAsync(new RuntimeModel(model.Id, model.FilePath), cancellationToken).ConfigureAwait(false);
                if (ActiveModelId is { } oldId && !oldId.Equals(model.Id, StringComparison.OrdinalIgnoreCase)
                    && models.TryGetValue(oldId, out var oldModel))
                    SetState(oldModel, ModelState.Installed);
                ActiveModelId = model.Id;
                SetState(model, ModelState.Ready);
            }
            catch
            {
                SetState(model, ModelState.LoadFailed);
                throw;
            }
            finally
            {
                jobQueue.Resume();
            }
        }
        finally
        {
            switchGate.Release();
        }
    }

    private ModelDefinition GetModel(string modelId) => models.TryGetValue(modelId, out var model)
        ? model
        : throw new KeyNotFoundException($"Model '{modelId}' is not registered.");

    private void SetState(ModelDefinition model, ModelState state)
    {
        states[model.Id] = state;
        StateChanged?.Invoke(this, new ModelStateChangedEventArgs(model, state));
    }

    private void OnRuntimeInterrupted(object? sender, RuntimeExitedEventArgs eventArgs)
    {
        if (ActiveModelId is { } activeId && models.TryGetValue(activeId, out var activeModel))
            SetState(activeModel, ModelState.RuntimeCrashed);
    }

    private void OnRuntimeStatusChanged(object? sender, RuntimeStatusChangedEventArgs eventArgs)
    {
        if (ActiveModelId is not { } activeId || !models.TryGetValue(activeId, out var activeModel))
            return;
        if (eventArgs.Status == RuntimeStatus.Ready)
            SetState(activeModel, ModelState.Ready);
        else if (eventArgs.Status == RuntimeStatus.Recovering)
            SetState(activeModel, ModelState.RuntimeCrashed);
        else if (eventArgs.Status == RuntimeStatus.Faulted)
            SetState(activeModel, ModelState.LoadFailed);
    }
}
