namespace MTranslate.Api;

public static class LocalApiConstants
{
    public const string ApiVersion = "1.0";
    public const string BasePath = "/api/v1";
    public static IReadOnlyList<int> DefaultPortPool { get; } = [17891, 17893, 17895, 17897, 17899];
}

public sealed record LocalApiGatewayOptions(
    string DatabasePath,
    IReadOnlyList<int>? PortPool = null,
    string AppVersion = "1.0.0",
    int MaximumRequestBytes = 32 * 1024,
    int MaximumBatchItems = 50,
    int MaximumBatchTokens = 32_000,
    int RequestsPerMinute = 120,
    int BatchRequestsPerMinute = 30,
    int PairingAttemptsPerMinute = 10)
{
    public IReadOnlyList<int> EffectivePortPool => PortPool is { Count: > 0 }
        ? PortPool
        : LocalApiConstants.DefaultPortPool;
}

public sealed record ApiTranslationCommand(
    string Text,
    string SourceLanguage,
    string TargetLanguage,
    string Mode,
    string? Context,
    bool PreserveLineBreaks,
    bool UseCache);

public sealed record ApiTranslationBackendResult(
    string Text,
    string DetectedLanguage,
    string Model,
    bool Cached,
    TimeSpan Elapsed);

public sealed record ApiModelDescriptor(string Id, string Name, bool Installed);

public interface ILocalApiTranslationBackend
{
    bool ModelLoaded { get; }
    string ActiveModelId { get; }
    IReadOnlyList<ApiModelDescriptor> Models { get; }

    Task<ApiTranslationBackendResult> TranslateAsync(
        ApiTranslationCommand command,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> TranslateStreamingAsync(
        ApiTranslationCommand command,
        CancellationToken cancellationToken = default);
}

public sealed record PairRequest(string Code, string ClientName, string ClientType);
public sealed record TranslateOptions(bool PreserveLineBreaks = true, bool UseCache = true);
public sealed record TranslateRequest(
    string Text,
    string SourceLanguage = "auto",
    string TargetLanguage = "zh-CN",
    string Mode = "standard",
    string? Context = null,
    TranslateOptions? Options = null);
public sealed record BatchItemRequest(string Id, string Text);
public sealed record BatchTranslateRequest(
    IReadOnlyList<BatchItemRequest> Items,
    string SourceLanguage = "auto",
    string TargetLanguage = "zh-CN",
    string Mode = "standard");

public sealed record ApiClient(
    Guid Id,
    string Name,
    string ClientType,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    string Permissions,
    bool Revoked);

public sealed record IssuedApiClient(ApiClient Client, string Token);

public interface IApiClientStore
{
    Task<IssuedApiClient> IssueAsync(
        string name,
        string clientType,
        string permissions = "translate",
        CancellationToken cancellationToken = default);
    Task<ApiClient?> ValidateAsync(string token, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApiClient>> ListAsync(CancellationToken cancellationToken = default);
    Task<bool> RevokeAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IPairingCodeManager
{
    PairingCode Create(TimeSpan? lifetime = null);
    bool Consume(string code);
    PairingCode? Current { get; }
}

public sealed record PairingCode(string Code, DateTimeOffset ExpiresAt);
