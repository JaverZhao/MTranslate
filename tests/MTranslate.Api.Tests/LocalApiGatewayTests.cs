using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MTranslate.Api.Tests;

public sealed class LocalApiGatewayTests
{
    [Fact]
    public async Task Gateway_RequiresPairingAndBearerTokenForTranslation()
    {
        await using var fixture = await GatewayFixture.CreateAsync();

        var health = await fixture.Client.GetAsync("health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        var healthJson = await health.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("1.0", healthJson.GetProperty("apiVersion").GetString());

        var unauthorized = await fixture.Client.GetAsync("info");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var pairing = fixture.Gateway.PairingCodes.Create();
        var paired = await fixture.Client.PostAsJsonAsync("pair", new PairRequest(pairing.Code, "Test Browser", "browser-extension"));
        Assert.Equal(HttpStatusCode.OK, paired.StatusCode);
        var pairJson = await paired.Content.ReadFromJsonAsync<JsonElement>();
        var token = pairJson.GetProperty("token").GetString();
        Assert.NotNull(token);
        fixture.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var info = await fixture.Client.GetFromJsonAsync<JsonElement>("info");
        Assert.Equal(38, info.GetProperty("supportedLanguages").GetArrayLength());

        var translated = await fixture.Client.PostAsJsonAsync("translate", new TranslateRequest("Hello", "en", "zh-CN"));
        Assert.Equal(HttpStatusCode.OK, translated.StatusCode);
        var translatedJson = await translated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("译:Hello", translatedJson.GetProperty("translatedText").GetString());
        Assert.Equal("en", translatedJson.GetProperty("detectedLanguage").GetString());
        Assert.Equal("Hello", fixture.Backend.LastCommand?.Text);
    }

    [Fact]
    public async Task Gateway_RejectsWebOriginsAndHostsOutsideAllowList()
    {
        await using var fixture = await GatewayFixture.CreateAsync();

        using var webRequest = new HttpRequestMessage(HttpMethod.Get, "health");
        webRequest.Headers.Add("Origin", "https://example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Client.SendAsync(webRequest)).StatusCode);

        using var extensionRequest = new HttpRequestMessage(HttpMethod.Get, "health");
        extensionRequest.Headers.Add("Origin", "chrome-extension://abcdefghijklmnop");
        var extensionResponse = await fixture.Client.SendAsync(extensionRequest);
        Assert.Equal(HttpStatusCode.OK, extensionResponse.StatusCode);
        Assert.Equal("chrome-extension://abcdefghijklmnop", extensionResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());

        using var badHost = new HttpRequestMessage(HttpMethod.Get, "health");
        badHost.Headers.Host = "evil.test";
        Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Client.SendAsync(badHost)).StatusCode);
    }

    [Fact]
    public async Task Gateway_EmitsStableSseEvents()
    {
        await using var fixture = await GatewayFixture.CreateAsync();
        await fixture.AuthorizeAsync();

        using var response = await fixture.Client.PostAsJsonAsync("translate/stream", new TranslateRequest("Hello", "en", "zh-CN"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("event: start", body, StringComparison.Ordinal);
        Assert.Contains("event: delta", body, StringComparison.Ordinal);
        Assert.Contains("event: complete", body, StringComparison.Ordinal);
        Assert.DoesNotContain("data: [DONE]", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gateway_RateLimitsEachAuthenticatedClient()
    {
        await using var fixture = await GatewayFixture.CreateAsync(requestsPerMinute: 2);
        await fixture.AuthorizeAsync();

        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync("info")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync("models")).StatusCode);
        var limited = await fixture.Client.GetAsync("info");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("60", limited.Headers.RetryAfter?.Delta?.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ?? limited.Headers.GetValues("Retry-After").Single());
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync("health")).StatusCode);
    }

    [Fact]
    public async Task Gateway_RejectsRequestsOverThirtyTwoKilobytes()
    {
        await using var fixture = await GatewayFixture.CreateAsync();
        await fixture.AuthorizeAsync();

        var response = await fixture.Client.PostAsJsonAsync(
            "translate",
            new TranslateRequest(new string('界', 11_000), "zh-CN", "en"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(fixture.Backend.LastCommand);
    }

    private sealed class GatewayFixture : IAsyncDisposable
    {
        private readonly string directory;

        private GatewayFixture(string directory, LocalApiGateway gateway, FakeBackend backend, HttpClient client)
        {
            this.directory = directory;
            Gateway = gateway;
            Backend = backend;
            Client = client;
        }

        public LocalApiGateway Gateway { get; }
        public FakeBackend Backend { get; }
        public HttpClient Client { get; }

        public static async Task<GatewayFixture> CreateAsync(int requestsPerMinute = 120)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"mtranslate-api-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var backend = new FakeBackend();
            var gateway = new LocalApiGateway(
                new LocalApiGatewayOptions(Path.Combine(directory, "app.db"), [FindAvailablePort()], RequestsPerMinute: requestsPerMinute),
                backend);
            await gateway.StartAsync();
            var client = new HttpClient { BaseAddress = new Uri(gateway.BaseUrl!.TrimEnd('/') + "/") };
            return new GatewayFixture(directory, gateway, backend, client);
        }

        public async Task AuthorizeAsync()
        {
            var pairing = Gateway.PairingCodes.Create();
            var response = await Client.PostAsJsonAsync("pair", new PairRequest(pairing.Code, "Test", "desktop"));
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Gateway.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }

        private static int FindAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    private sealed class FakeBackend : ILocalApiTranslationBackend
    {
        public bool ModelLoaded => true;
        public string ActiveModelId => "hy-mt2-1.8b-q4";
        public IReadOnlyList<ApiModelDescriptor> Models { get; } = [new("hy-mt2-1.8b-q4", "Standard", true)];
        public ApiTranslationCommand? LastCommand { get; private set; }

        public Task<ApiTranslationBackendResult> TranslateAsync(ApiTranslationCommand command, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            var detected = command.SourceLanguage == "auto" ? "en" : command.SourceLanguage;
            return Task.FromResult(new ApiTranslationBackendResult("译:" + command.Text, detected, ActiveModelId, false, TimeSpan.FromMilliseconds(5)));
        }

        public async IAsyncEnumerable<string> TranslateStreamingAsync(
            ApiTranslationCommand command,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return "译:";
            yield return command.Text;
        }
    }
}
