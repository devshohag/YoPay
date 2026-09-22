using YoPay.Domain.Common;
using YoPay.Domain.Enums;

namespace YoPay.Domain.Entities;

/// <summary>
/// A transaction id already seen, stored as a hash.
///
/// Hashed rather than raw because this table is the basis of the cross-merchant fraud
/// signal, and one merchant's transaction identifiers are not another's to read.
///
/// A hit does not auto-reject. It raises the payment for review, because the common
/// cause of a repeat is a retry, not a fraud attempt.
/// </summary>
public class FraudSignal : Entity
{
    public PaymentMethod Method { get; set; }

    /// <summary>SHA-256 of the transaction id, normalised to upper case.</summary>
    public string TrxIdHash { get; set; } = null!;

    public FraudSignalReason Reason { get; set; }
    public Guid? FirstSeenMerchantId { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Note { get; set; }
}
