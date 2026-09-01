using MTranslate.Api;

namespace MTranslate.Desktop.Services;

public interface ILocalApiService
{
    bool IsRunning { get; }
    string Endpoint { get; }
    string? LastError { get; }
    PairingCode CreatePairingCode();
    Task<IReadOnlyList<ApiClient>> ListClientsAsync(CancellationToken cancellationToken = default);
    Task<bool> RevokeClientAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class LocalApiService : ILocalApiService, IDisposable
{
    private readonly LocalApiGateway gateway;
    private bool disposed;

    public LocalApiService(ILocalApiTranslationBackend backend)
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MTranslate");
        gateway = new LocalApiGateway(
            new LocalApiGatewayOptions(Path.Combine(dataDirectory, "database", "app.db")),
            backend);
    }

    public bool IsRunning => gateway.IsRunning;
    public string Endpoint => gateway.BaseUrl ?? "未监听";
    public string? LastError { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await gateway.StartAsync(cancellationToken).ConfigureAwait(false);
            LastError = null;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
        }
    }

    public PairingCode CreatePairingCode() => gateway.PairingCodes.Create();
    public Task<IReadOnlyList<ApiClient>> ListClientsAsync(CancellationToken cancellationToken = default) =>
        gateway.ClientStore.ListAsync(cancellationToken);
    public Task<bool> RevokeClientAsync(Guid id, CancellationToken cancellationToken = default) =>
        gateway.ClientStore.RevokeAsync(id, cancellationToken);

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            gateway.StopAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // App shutdown must not be held open by a stalled local HTTP connection.
        }
        gateway.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
