using YoPay.Application.Abstractions;

namespace YoPay.UnitTests;

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

/// <summary>Records what it was asked, so a test can assert on what was NOT asked.</summary>
internal sealed class RecordingNonceStore : INonceStore
{
    private readonly HashSet<string> _seen = [];

    public int ConsumeAttempts { get; private set; }

    public Task<bool> TryConsumeAsync(
        string keyId, string nonce, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        ConsumeAttempts++;
        return Task.FromResult(_seen.Add($"{keyId}|{nonce}"));
    }

    public Task<int> PruneExpiredAsync(DateTimeOffset asOf, CancellationToken ct = default) =>
        Task.FromResult(0);
}
