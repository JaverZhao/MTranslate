using Microsoft.Data.Sqlite;

namespace MTranslate.Api.Tests;

public sealed class PairingAndClientStoreTests
{
    [Fact]
    public void PairingCode_IsSixDigitsAndCanOnlyBeConsumedOnce()
    {
        var manager = new PairingCodeManager();

        var pairing = manager.Create();

        Assert.Matches("^[0-9]{6}$", pairing.Code);
        Assert.True(manager.Consume(pairing.Code));
        Assert.False(manager.Consume(pairing.Code));
        Assert.Null(manager.Current);
    }

    [Fact]
    public void PairingCode_ExpiresAtConfiguredDeadline()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 28, 0, 0, 0, TimeSpan.Zero));
        var manager = new PairingCodeManager(clock);
        var pairing = manager.Create(TimeSpan.FromMinutes(5));

        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.False(manager.Consume(pairing.Code));
        Assert.Null(manager.Current);
    }

    [Fact]
    public async Task ClientStore_PersistsOnlyTokenHashAndSupportsRevocation()
    {
        var directory = CreateDirectory();
        var database = Path.Combine(directory, "app.db");
        try
        {
            await using var store = new SqliteApiClientStore(database);
            var issued = await store.IssueAsync("Browser", "browser-extension");

            Assert.Equal(43, issued.Token.Length);
            Assert.NotNull(await store.ValidateAsync(issued.Token));
            Assert.True(await store.RevokeAsync(issued.Client.Id));
            Assert.Null(await store.ValidateAsync(issued.Token));
            await store.DisposeAsync();

            await using var connection = new SqliteConnection($"Data Source={database};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT TokenHash FROM ApiClient WHERE Id = $id";
            command.Parameters.AddWithValue("$id", issued.Client.Id.ToString("D"));
            var stored = Assert.IsType<string>(await command.ExecuteScalarAsync());
            Assert.Equal(64, stored.Length);
            Assert.DoesNotContain(issued.Token, stored, StringComparison.Ordinal);
            await command.DisposeAsync();
            await connection.DisposeAsync();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mtranslate-api-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current += duration;
    }
}
