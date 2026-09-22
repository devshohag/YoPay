namespace YoPay.Application.Devices;

/// <summary>
/// How often a paired phone reports in, and when silence counts as offline.
///
/// An earlier draft used a flat thirty seconds so the alert would land inside a
/// two-minute promise - which was picking an engineering number to fit a marketing one.
/// Thirty-second wake-ups all day drain the battery and attract exactly the OEM power
/// management that kills the listener, so the cure caused the disease.
///
/// The interval now follows the work: often while a payment is actually in flight,
/// rarely when the phone has nothing to watch. The alert threshold follows from that
/// rather than the other way round, and the SLA states what it really is.
/// </summary>
public sealed record HeartbeatPolicy
{
    public static readonly HeartbeatPolicy Default = new()
    {
        ActiveInterval = TimeSpan.FromSeconds(60),
        IdleInterval = TimeSpan.FromMinutes(3),
        MissesBeforeOffline = 3,
        Grace = TimeSpan.FromSeconds(30),
    };

    /// <summary>Used while the wallet has at least one open payment session.</summary>
    public required TimeSpan ActiveInterval { get; init; }

    public required TimeSpan IdleInterval { get; init; }
    public required int MissesBeforeOffline { get; init; }

    /// <summary>Absorbs a slow network so one bad minute is not an outage alert.</summary>
    public required TimeSpan Grace { get; init; }

    /// <summary>Roughly 3.5 minutes active, 9.5 minutes idle. These are the numbers the
    /// status page and the SLA must quote - not a rounder, nicer pair.</summary>
    public TimeSpan SilenceThreshold(bool hasOpenSession) =>
        (hasOpenSession ? ActiveInterval : IdleInterval) * MissesBeforeOffline + Grace;

    public bool IsOffline(DateTimeOffset? lastHeartbeatAt, DateTimeOffset asOf, bool hasOpenSession) =>
        lastHeartbeatAt is null || asOf - lastHeartbeatAt.Value > SilenceThreshold(hasOpenSession);
}
