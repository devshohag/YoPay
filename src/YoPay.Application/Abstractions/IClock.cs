namespace YoPay.Application.Abstractions;

/// <summary>
/// Server time, injected rather than read from DateTimeOffset.UtcNow.
///
/// Every window, grace period and expiry test in this product is a time comparison, and
/// a rule that can only be tested by waiting is a rule nobody tests. Note that this is
/// the server clock: the matcher compares against the device-reported event time, and
/// uses this only for bookkeeping.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
