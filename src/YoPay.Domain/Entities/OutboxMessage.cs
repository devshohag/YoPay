using YoPay.Domain.Common;
using YoPay.Domain.Enums;

namespace YoPay.Domain.Entities;

/// <summary>
/// Work queued inside the same transaction that changed the data.
///
/// Writing the match and the notification together is what stops the two classic
/// failures: an invoice marked paid whose webhook never fires, and a webhook that fires
/// for a match that rolled back.
/// </summary>
public class OutboxMessage : Entity
{
    public string Type { get; set; } = null!;
    public string PayloadJson { get; set; } = null!;

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public string? LastError { get; set; }
}
