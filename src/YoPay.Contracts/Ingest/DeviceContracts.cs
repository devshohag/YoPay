using YoPay.Domain.Enums;

namespace YoPay.Contracts.Ingest;

/// <summary>
/// A batch of observations uploaded by a paired phone.
///
/// Uploads are at-least-once by design: the device keeps its queue until the server
/// acknowledges, so a dropped connection costs a duplicate rather than a lost payment.
/// The server deduplicates on the hash, which is the cheap half of the bargain.
/// </summary>
public sealed record DeviceEventBatch
{
    public required string DeviceFingerprint { get; init; }
    public required IReadOnlyList<DeviceEvent> Events { get; init; }
}

public sealed record DeviceEvent
{
    public required EventSource Source { get; init; }
    public required string SenderId { get; init; }
    public required string Body { get; init; }

    /// <summary>When the phone saw it. This is the clock the matcher trusts.</summary>
    public required DateTimeOffset ReceivedAt { get; init; }

    /// <summary>Client-side hash, recomputed server-side and never trusted as given.</summary>
    public required string DedupeHash { get; init; }
}

public sealed record DeviceHeartbeat
{
    public required string DeviceFingerprint { get; init; }
    public required string AppVersion { get; init; }
    public required DevicePermissionState PermissionState { get; init; }
    public int? BatteryPercent { get; init; }
    public string? NetworkType { get; init; }
    public required DateTimeOffset SentAt { get; init; }
}

public sealed record DeviceIngestResponse
{
    public required int Accepted { get; init; }
    public required int Duplicates { get; init; }
}
