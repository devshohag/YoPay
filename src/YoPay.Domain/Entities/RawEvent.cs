using YoPay.Domain.Common;
using YoPay.Domain.Enums;

namespace YoPay.Domain.Entities;

/// <summary>
/// A message a device saw, stored exactly as received and never edited. Partitioned by
/// month and archived after ninety days.
///
/// DeviceReceivedAt is the trusted event time and the only clock the matcher reads.
/// Using server time instead would mark a perfectly timely payment as expired whenever
/// a phone had been offline and uploaded its backlog late.
///
/// DedupeHash carries a unique index, which is what makes at-least-once delivery from
/// the device safe: the same message uploaded ten times is ingested once.
/// </summary>
public class RawEvent : MerchantEntity
{
    public Guid DeviceId { get; set; }
    public EventSource Source { get; set; }

    /// <summary>Operator sender id, checked against a whitelist before anything else.</summary>
    public string SenderId { get; set; } = null!;

    public string Body { get; set; } = null!;

    public DateTimeOffset DeviceReceivedAt { get; set; }
    public DateTimeOffset ServerReceivedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>SHA-256 over device, sender, body and device timestamp.</summary>
    public string DedupeHash { get; set; } = null!;

    public RawEventState State { get; set; } = RawEventState.Received;

    public Device Device { get; set; } = null!;
}
