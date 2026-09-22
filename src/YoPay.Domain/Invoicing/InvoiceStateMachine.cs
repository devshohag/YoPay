using YoPay.Domain.Common;
using YoPay.Domain.Enums;

namespace YoPay.Domain.Invoicing;

/// <summary>
/// The only place an invoice status is allowed to change.
///
/// Two rules are encoded here and nowhere else:
///
/// 1. An invoice never fails outright. When a window elapses it goes to PendingReview,
///    because provider messages arrive minutes late and a device that was offline
///    uploads its backlog hours late. Only the scheduler, after the grace period,
///    moves PendingReview to Expired.
///
/// 2. Paid is terminal apart from Settled. Nothing walks a paid invoice backwards, so a
///    late duplicate message cannot reopen an order that has already shipped.
/// </summary>
public static class InvoiceStateMachine
{
    private static readonly Dictionary<InvoiceStatus, InvoiceStatus[]> Allowed = new()
    {
        [InvoiceStatus.Created] =
        [
            InvoiceStatus.AwaitingPayment,
            InvoiceStatus.Cancelled,
        ],
        [InvoiceStatus.AwaitingPayment] =
        [
            InvoiceStatus.Paid,
            InvoiceStatus.Partial,
            InvoiceStatus.PendingReview,
            InvoiceStatus.Cancelled,
        ],
        [InvoiceStatus.Partial] =
        [
            InvoiceStatus.Paid,
            InvoiceStatus.PendingReview,
            InvoiceStatus.Cancelled,
        ],
        [InvoiceStatus.PendingReview] =
        [
            InvoiceStatus.Paid,
            InvoiceStatus.Partial,
            InvoiceStatus.Expired,
            InvoiceStatus.Cancelled,
        ],
        [InvoiceStatus.Paid] =
        [
            InvoiceStatus.Settled,
        ],
        [InvoiceStatus.Expired] = [],
        [InvoiceStatus.Cancelled] = [],
        [InvoiceStatus.Settled] = [],
    };

    public static bool CanTransition(InvoiceStatus from, InvoiceStatus to) =>
        Allowed.TryGetValue(from, out var targets) && Array.IndexOf(targets, to) >= 0;

    public static IReadOnlyCollection<InvoiceStatus> NextStates(InvoiceStatus from) =>
        Allowed.TryGetValue(from, out var targets) ? targets : [];

    public static bool IsTerminal(InvoiceStatus status) =>
        Allowed.TryGetValue(status, out var targets) && targets.Length == 0;

    /// <summary>Applies the transition or throws. Callers never set Status directly.</summary>
    public static void Transition(Entities.Invoice invoice, InvoiceStatus to, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        if (invoice.Status == to)
        {
            return; // idempotent: replaying the same event must not throw
        }

        if (!CanTransition(invoice.Status, to))
        {
            throw new DomainException(
                $"Invoice {invoice.Id} cannot move from {invoice.Status} to {to}.");
        }

        invoice.Status = to;
        invoice.UpdatedAt = asOf;

        if (to == InvoiceStatus.Paid)
        {
            invoice.PaidAt = asOf;
        }
    }
}
