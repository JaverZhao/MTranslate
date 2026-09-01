using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MTranslate.Core;

namespace MTranslate.Api;

public sealed class LocalApiGateway : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly LocalApiGatewayOptions options;
    private readonly ILocalApiTranslationBackend backend;
    private readonly SqliteApiClientStore clientStore;
    private readonly IPairingCodeManager pairingCodes;
    private readonly ClientRateLimiter rateLimiter = new();
    private WebApplication? application;

    public LocalApiGateway(
        LocalApiGatewayOptions options,
        ILocalApiTranslationBackend backend,
        IPairingCodeManager? pairingCodes = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        this.pairingCodes = pairingCodes ?? new PairingCodeManager();
        clientStore = new SqliteApiClientStore(options.DatabasePath);
    }

    public bool IsRunning => application is not null;
    public int? Port { get; private set; }
    public string? BaseUrl => Port is { } port ? $"http://127.0.0.1:{port}{LocalApiConstants.BasePath}" : null;
    public IApiClientStore ClientStore => clientStore;
    public IPairingCodeManager PairingCodes => pairingCodes;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (application is not null)
            return;
        var port = SelectPort(options.EffectivePortPool);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.AddServerHeader = false;
            server.Limits.MaxRequestBodySize = 2 * 1024 * 1024;
            server.Listen(IPAddress.Loopback, port);
        });
        builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        var app = builder.Build();
        ConfigurePipeline(app);
        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            application = app;
            Port = port;
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (application is null)
            return;
        var app = application;
        application = null;
        Port = null;
        try
        {
            await app.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await clientStore.DisposeAsync().ConfigureAwait(false);
    }

    private void ConfigurePipeline(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context).ConfigureAwait(false);
            }
            catch (BadHttpRequestException exception)
            {
                await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "invalid_request", exception.Message).ConfigureAwait(false);
            }
            catch (ArgumentException exception)
            {
                await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "invalid_request", exception.Message).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                await WriteProblemAsync(context, StatusCodes.Status409Conflict, "service_unavailable", exception.Message).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The caller disconnected; there is no response channel left to write to.
            }
            catch (Exception)
            {
                await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "internal_error", "The local API request failed.").ConfigureAwait(false);
            }
        });

        app.Use(async (context, next) =>
        {
            var host = context.Request.Host.Host;
            if (!host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "invalid_host", "The Host header is not allowed.").ConfigureAwait(false);
                return;
            }

            var origin = context.Request.Headers.Origin.ToString();
            if (origin.Length > 0)
            {
                if (!IsAllowedExtensionOrigin(origin))
                {
                    await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "origin_forbidden", "The Origin is not allowed.").ConfigureAwait(false);
                    return;
                }
                context.Response.Headers.AccessControlAllowOrigin = origin;
                context.Response.Headers.Vary = "Origin";
                context.Response.Headers.AccessControlAllowHeaders = "Authorization, Content-Type";
                context.Response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS";
            }
            if (HttpMethods.IsOptions(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
            if (context.Request.Path.Equals($"{LocalApiConstants.BasePath}/pair")
                && !rateLimiter.TryAcquire(Guid.Empty, "pair", options.PairingAttemptsPerMinute))
            {
                context.Response.Headers.RetryAfter = "60";
                await WriteProblemAsync(context, StatusCodes.Status429TooManyRequests, "rate_limit_exceeded", "Too many pairing attempts.").ConfigureAwait(false);
                return;
            }
            await next(context).ConfigureAwait(false);
        });

        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            if (path.Equals($"{LocalApiConstants.BasePath}/health") || path.Equals($"{LocalApiConstants.BasePath}/pair"))
            {
                await next(context).ConfigureAwait(false);
                return;
            }
            if (!AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out var authorization)
                || !authorization.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(authorization.Parameter)
                || await clientStore.ValidateAsync(authorization.Parameter, context.RequestAborted).ConfigureAwait(false) is not { } client)
            {
                context.Response.Headers.WWWAuthenticate = "Bearer";
                await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, "unauthorized", "A valid bearer token is required.").ConfigureAwait(false);
                return;
            }
            context.Items[typeof(ApiClient)] = client;
            await next(context).ConfigureAwait(false);
        });

        app.Use(async (context, next) =>
        {
            if (context.Items[typeof(ApiClient)] is not ApiClient client)
            {
                await next(context).ConfigureAwait(false);
                return;
            }
            var isBatch = context.Request.Path.Equals($"{LocalApiConstants.BasePath}/translate/batch");
            var limit = isBatch ? options.BatchRequestsPerMinute : options.RequestsPerMinute;
            var bucket = isBatch ? "batch" : "general";
            if (!rateLimiter.TryAcquire(client.Id, bucket, limit))
            {
                context.Response.Headers.RetryAfter = "60";
                await WriteProblemAsync(context, StatusCodes.Status429TooManyRequests, "rate_limit_exceeded", "Too many requests from this API client.").ConfigureAwait(false);
                return;
            }
            await next(context).ConfigureAwait(false);
        });

        var group = app.MapGroup(LocalApiConstants.BasePath);
        group.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            version = options.AppVersion,
            apiVersion = LocalApiConstants.ApiVersion,
            engine = "llama.cpp",
            modelLoaded = backend.ModelLoaded,
            model = backend.ActiveModelId
        }));

        group.MapPost("/pair", async (PairRequest request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.ClientName) || request.ClientName.Trim().Length > 100
                || string.IsNullOrWhiteSpace(request.ClientType) || request.ClientType.Trim().Length > 50)
                return Results.BadRequest(new { success = false, error = "invalid_client" });
            if (!pairingCodes.Consume(request.Code))
                return Results.Json(new { success = false, error = "invalid_or_expired_code" }, statusCode: StatusCodes.Status401Unauthorized);
            var issued = await clientStore.IssueAsync(request.ClientName, request.ClientType, cancellationToken: cancellationToken).ConfigureAwait(false);
            return Results.Ok(new { success = true, token = issued.Token, apiVersion = LocalApiConstants.ApiVersion });
        });

        group.MapGet("/info", () => Results.Ok(new
        {
            appVersion = options.AppVersion,
            apiVersion = LocalApiConstants.ApiVersion,
            activeModel = backend.ActiveModelId,
            supportedLanguages = TranslationLanguages.All.Select(language => new { language.Code, name = language.EnglishName }),
            streaming = true,
            batch = true
        }));

        group.MapGet("/models", () => Results.Ok(new { active = backend.ActiveModelId, models = backend.Models }));

        group.MapPost("/translate", async (TranslateRequest request, CancellationToken cancellationToken) =>
        {
            var command = Validate(request);
            var requestId = Guid.NewGuid();
            var result = await backend.TranslateAsync(command, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new
            {
                requestId,
                translatedText = result.Text,
                detectedLanguage = result.DetectedLanguage,
                targetLanguage = command.TargetLanguage,
                model = result.Model,
                cached = result.Cached,
                elapsedMs = Math.Round(result.Elapsed.TotalMilliseconds)
            });
        });

        group.MapPost("/translate/batch", async (BatchTranslateRequest request, CancellationToken cancellationToken) =>
        {
            if (request.Items is null || request.Items.Count is < 1 || request.Items.Count > options.MaximumBatchItems)
                return Results.BadRequest(new { error = $"items must contain 1 to {options.MaximumBatchItems} entries" });
            if (request.Items.Any(item => string.IsNullOrWhiteSpace(item.Id))
                || request.Items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != request.Items.Count)
                return Results.BadRequest(new { error = "item ids must be non-empty and unique" });
            if (request.Items.Sum(item => Encoding.UTF8.GetByteCount(item.Text ?? string.Empty)) > options.MaximumRequestBytes)
                return Results.BadRequest(new { error = "batch text is too large" });
            var tokenEstimator = new HeuristicTokenEstimator();
            if (request.Items.Sum(item => tokenEstimator.Estimate(item.Text ?? string.Empty)) > options.MaximumBatchTokens)
                return Results.BadRequest(new { error = "batch token estimate is too large" });

            var results = new List<object>(request.Items.Count);
            foreach (var item in request.Items)
            {
                var command = Validate(new TranslateRequest(item.Text, request.SourceLanguage, request.TargetLanguage, request.Mode));
                var translated = await backend.TranslateAsync(command, cancellationToken).ConfigureAwait(false);
                results.Add(new { item.Id, translatedText = translated.Text, detectedLanguage = translated.DetectedLanguage });
            }
            return Results.Ok(new { items = results });
        });

        group.MapPost("/translate/stream", StreamAsync);
    }

    private async Task StreamAsync(HttpContext context, TranslateRequest request, CancellationToken cancellationToken)
    {
        var command = Validate(request);
        var requestId = Guid.NewGuid();
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        await WriteEventAsync(context, "start", new { requestId }, cancellationToken).ConfigureAwait(false);
        try
        {
            await foreach (var delta in backend.TranslateStreamingAsync(command, cancellationToken).ConfigureAwait(false))
                await WriteEventAsync(context, "delta", new { requestId, text = delta }, cancellationToken).ConfigureAwait(false);
            await WriteEventAsync(context, "complete", new { requestId }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await WriteEventAsync(context, "error", new { requestId, error = "translation_failed" }, cancellationToken).ConfigureAwait(false);
        }
    }

    private ApiTranslationCommand Validate(TranslateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("text must not be empty");
        if (Encoding.UTF8.GetByteCount(request.Text) > options.MaximumRequestBytes)
            throw new ArgumentException("text is too large");
        var source = string.IsNullOrWhiteSpace(request.SourceLanguage) ? "auto" : request.SourceLanguage.Trim();
        var target = request.TargetLanguage?.Trim() ?? string.Empty;
        if (!IsSupportedLanguage(target))
            throw new ArgumentException("targetLanguage is not supported");
        if (!source.Equals("auto", StringComparison.OrdinalIgnoreCase) && !IsSupportedLanguage(source))
            throw new ArgumentException("sourceLanguage is not supported");
        var mode = string.IsNullOrWhiteSpace(request.Mode) ? "standard" : request.Mode.Trim().ToLowerInvariant();
        if (mode is not ("standard" or "fast"))
            throw new ArgumentException("mode must be standard or fast");
        var requestOptions = request.Options ?? new TranslateOptions();
        return new ApiTranslationCommand(request.Text, source, target, mode, request.Context, requestOptions.PreserveLineBreaks, requestOptions.UseCache);
    }

    private static bool IsSupportedLanguage(string code) => TranslationLanguages.All.Any(
        language => language.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    private static async Task WriteEventAsync(HttpContext context, string eventName, object payload, CancellationToken cancellationToken)
    {
        await context.Response.WriteAsync($"event: {eventName}\n", cancellationToken).ConfigureAwait(false);
        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(payload, JsonOptions)}\n\n", cancellationToken).ConfigureAwait(false);
        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteProblemAsync(HttpContext context, int status, string title, string detail)
    {
        if (context.Response.HasStarted)
            return;
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new { type = $"urn:mtranslate:error:{title}", title, status, detail }).ConfigureAwait(false);
    }

    private static bool IsAllowedExtensionOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.Scheme.Equals("chrome-extension", StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals("moz-extension", StringComparison.OrdinalIgnoreCase))
        && !string.IsNullOrWhiteSpace(uri.Host)
        && uri.UserInfo.Length == 0;

    private static int SelectPort(IReadOnlyList<int> portPool)
    {
        foreach (var port in portPool)
        {
            if (port is < 1 or > 65535)
                throw new ArgumentOutOfRangeException(nameof(portPool), "Ports must be between 1 and 65535.");
            TcpListener? listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return port;
            }
            catch (SocketException)
            {
                // Try the next reserved loopback port.
            }
            finally
            {
                listener?.Stop();
            }
        }
        throw new InvalidOperationException("No available Local API port was found in the reserved pool.");
    }

    private sealed class ClientRateLimiter
    {
        private readonly object sync = new();
        private readonly Dictionary<(Guid Client, string Bucket), Window> windows = [];

        public bool TryAcquire(Guid client, string bucket, int limit)
        {
            if (limit <= 0)
                return false;
            var now = DateTimeOffset.UtcNow;
            lock (sync)
            {
                var key = (client, bucket);
                if (!windows.TryGetValue(key, out var window) || now - window.Start >= TimeSpan.FromMinutes(1))
                {
                    windows[key] = new Window(now, 1);
                    return true;
                }
                if (window.Count >= limit)
                    return false;
                windows[key] = window with { Count = window.Count + 1 };
                return true;
            }
        }

        private sealed record Window(DateTimeOffset Start, int Count);
    }
}
