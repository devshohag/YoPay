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
public class OutboxMessage : MerchantEntity
{
    public string Type { get; set; } = null!;
    public string PayloadJson { get; set; } = null!;

    /// <summary>
    /// Which invoice this is about, beside the payload rather than inside it.
    ///
    /// The payload is the merchant's document and its shape is a published contract; the
    /// dispatcher needs to find endpoints, group retries and show a delivery against an
    /// invoice, and none of that should require parsing JSON in a WHERE clause.
    /// </summary>
    public Guid InvoiceId { get; set; }

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public string? LastError { get; set; }
}
