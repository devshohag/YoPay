using YoPay.Application.Abstractions;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;
using YoPay.Domain.Invoicing;

namespace YoPay.Application.Invoicing;

public enum CancelInvoiceOutcome
{
    Cancelled,

    /// <summary>It was already cancelled. Answered as success, not as an error.</summary>
    AlreadyCancelled,

    NotFound,

    /// <summary>Paid, expired, or otherwise past the point of being called off.</summary>
    Refused,
}

public sealed record CancelInvoiceResult
{
    public required CancelInvoiceOutcome Outcome { get; init; }
    public Invoice? Invoice { get; init; }
    public string? Reason { get; init; }

    public bool Succeeded =>
        Outcome is CancelInvoiceOutcome.Cancelled or CancelInvoiceOutcome.AlreadyCancelled;
}

/// <summary>
/// Calls off an invoice the customer never paid.
///
/// Cancelling is not only bookkeeping. While an invoice is open its session holds a
/// reserved amount on the wallet, and that reservation is what makes an incoming amount
/// unambiguous - one open session per wallet per amount. A shop that abandons fifty carts
/// an hour and never cancels them slowly fills its own wallet with reservations until
/// ordinary amounts stop being available, so closing the session is the point of this
/// operation rather than a side effect of it.
///
/// Cancelling twice is success, not an error. The caller is a retrying HTTP client, and
/// the state it asked for is the state it got.
///
/// A paid invoice is refused outright. Money has arrived; whatever the merchant wants to
/// do about it is a refund, which happens in the real world between two people and a
/// wallet, not by moving a row backwards in this table.
/// </summary>
public sealed class CancelInvoiceService(IInvoiceStore store, IClock clock)
{
    public async Task<CancelInvoiceResult> CancelAsync(
        Guid merchantId,
        Guid? invoiceId,
        string? orderRef,
        CancellationToken ct = default)
    {
        var invoice = invoiceId is { } id
            ? await store.FindByIdAsync(merchantId, id, ct).ConfigureAwait(false)
            : await store.FindByOrderRefAsync(merchantId, orderRef ?? "", ct).ConfigureAwait(false);

        if (invoice is null)
        {
            return new CancelInvoiceResult { Outcome = CancelInvoiceOutcome.NotFound };
        }

        if (invoice.Status == InvoiceStatus.Cancelled)
        {
            return new CancelInvoiceResult
            {
                Outcome = CancelInvoiceOutcome.AlreadyCancelled,
                Invoice = invoice,
            };
        }

        if (!InvoiceStateMachine.CanTransition(invoice.Status, InvoiceStatus.Cancelled))
        {
            return new CancelInvoiceResult
            {
                Outcome = CancelInvoiceOutcome.Refused,
                Invoice = invoice,
                Reason = invoice.Status == InvoiceStatus.Paid
                    ? "This invoice has been paid. Cancelling it would not return the money; refund the customer instead."
                    : $"An invoice that is {invoice.Status} cannot be cancelled.",
            };
        }

        InvoiceStateMachine.Transition(invoice, InvoiceStatus.Cancelled, clock.UtcNow);

        await store.CancelAsync(invoice, ct).ConfigureAwait(false);

        return new CancelInvoiceResult
        {
            Outcome = CancelInvoiceOutcome.Cancelled,
            Invoice = invoice,
        };
    }
}
