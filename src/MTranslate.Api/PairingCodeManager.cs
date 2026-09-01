using System.Security.Cryptography;

namespace MTranslate.Api;

public sealed class PairingCodeManager(TimeProvider? timeProvider = null) : IPairingCodeManager
{
    private readonly object sync = new();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private PairingCode? current;

    public PairingCode? Current
    {
        get
        {
            lock (sync)
                return current is not null && current.ExpiresAt > clock.GetUtcNow() ? current : null;
        }
    }

    public PairingCode Create(TimeSpan? lifetime = null)
    {
        var validity = lifetime ?? TimeSpan.FromMinutes(5);
        if (validity <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        var pairing = new PairingCode(RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6"), clock.GetUtcNow() + validity);
        lock (sync)
            current = pairing;
        return pairing;
    }

    public bool Consume(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;
        lock (sync)
        {
            if (current is null || current.ExpiresAt <= clock.GetUtcNow())
            {
                current = null;
                return false;
            }
            var supplied = System.Text.Encoding.UTF8.GetBytes(code);
            var expected = System.Text.Encoding.UTF8.GetBytes(current.Code);
            var matches = supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
            if (matches)
                current = null;
            return matches;
        }
    }
}
