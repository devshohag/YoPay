using YoPay.Domain.Enums;

namespace YoPay.Application.Matching;

/// <summary>
/// An open session that could be the home for an incoming payment, flattened to just the
/// fields the decision needs.
///
/// A projection rather than the entity because the matcher runs inside a transaction
/// holding a lock, and the less it drags into memory there the shorter that lock is held.
/// </summary>
public sealed record MatchCandidate
{
    public required Guid SessionId { get; init; }
    public required Guid InvoiceId { get; init; }
    public required decimal ExpectedAmount { get; init; }
    public required DateTimeOffset OpenedAt { get; init; }
    public required DateTimeOffset GraceUntil { get; init; }
    public required InvoiceStatus InvoiceStatus { get; init; }

    /// <summary>Set when the candidate came from a customer's typed transaction id.</summary>
    public Guid? ClaimId { get; init; }

    /// <summary>
    /// How much earlier than OpenedAt a payment may be stamped and still belong here.
    ///
    /// bKash prints its timestamps to the minute: "at 24/09/2026 23:05" and nothing
    /// finer. A payment made at 23:05:41 therefore arrives stamped 23:05:00. If the
    /// invoice was created at 23:05:18 - the customer clicked pay a few seconds after the
    /// page loaded, which is the single most ordinary thing that happens here - then the
    /// payment's own timestamp is 18 seconds BEFORE the session opened, and a strict
    /// comparison throws it out. The invoice stays unpaid with an open window and a
    /// perfectly good payment sitting beside it.
    ///
    /// One minute would cover the truncation exactly. Two are allowed because the
    /// operator's clock and ours are not the same clock, and neither is wrong enough to
    /// refuse money over.
    ///
    /// The upper bound gets no such allowance: GraceUntil is already a deliberate
    /// extension past expiry, and stretching it further is a decision for the operator to
    /// make in the invoice, not for this comparison to make quietly.
    /// </summary>
    public static readonly TimeSpan OpeningTolerance = TimeSpan.FromMinutes(2);

    public bool Accepts(DateTimeOffset eventTime) =>
        eventTime >= OpenedAt - OpeningTolerance && eventTime <= GraceUntil;

    /// <summary>Which side of the window the payment fell on, for the message a person reads.</summary>
    public bool IsBeforeOpening(DateTimeOffset eventTime) =>
        eventTime < OpenedAt - OpeningTolerance;
}
