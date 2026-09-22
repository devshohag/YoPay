using YoPay.Application.Abstractions;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;
using YoPay.Domain.Invoicing;

namespace YoPay.Application.Invoicing;

/// <summary>
/// Turns a merchant's request into an invoice and the session that will match a payment
/// to it.
///
/// Retrying a create returns the original invoice rather than making a second one. That
/// is not a nicety: merchants retry on timeouts, and a duplicate invoice means a customer
/// paying twice for one order. The unique index on (merchant_id, order_ref) is the
/// backstop; this is the path that keeps it from being hit.
/// </summary>
public sealed class CreateInvoiceService(IInvoiceStore store, IClock clock)
{
    private static readonly InvoiceWindow Window = InvoiceWindow.Default;

    public async Task<CreateInvoiceResult> CreateAsync(
        CreateInvoiceCommand command,
        MatchingMode requestedMode = MatchingMode.TrxId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Amount <= 0m)
        {
            return new CreateInvoiceResult
            {
                Outcome = CreateInvoiceOutcome.InvalidAmount,
                Reason = "Amount must be greater than zero.",
            };
        }

        var existing = await store
            .FindByOrderRefAsync(command.MerchantId, command.OrderRef, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return new CreateInvoiceResult
            {
                Outcome = CreateInvoiceOutcome.AlreadyExists,
                Invoice = existing,
            };
        }

        var wallet = await store
            .FindWalletAsync(command.MerchantId, command.WalletId, command.Method, ct)
            .ConfigureAwait(false);

        if (wallet is null)
        {
            return new CreateInvoiceResult
            {
                Outcome = CreateInvoiceOutcome.NoWallet,
                Reason = $"No active {command.Method} wallet is configured for this merchant.",
            };
        }

        var now = clock.UtcNow;
        var (expiresAt, graceUntil) = Window.Apply(now);

        var mode = requestedMode;
        var chargedAmount = command.Amount;

        if (mode == MatchingMode.UniqueAmount)
        {
            var taken = await store.OpenSessionAmountsAsync(wallet.Id, ct).ConfigureAwait(false);
            var allocated = AmountAllocator.Allocate(command.Amount, taken);

            if (allocated is null)
            {
                // Every distinct figure within the salt ceiling is already expected by
                // another open session on this wallet. Widening the salt would start
                // distorting the price, so the invoice falls back to the flow that does
                // not depend on the amount being unique.
                mode = MatchingMode.TrxId;
            }
            else
            {
                chargedAmount = allocated.Value;
            }
        }

        var invoice = new Invoice
        {
            MerchantId = command.MerchantId,
            WalletId = wallet.Id,
            OrderRef = command.OrderRef,
            Amount = command.Amount,
            ChargedAmount = chargedAmount,
            Status = InvoiceStatus.AwaitingPayment,
            CreatedAt = now,
            ExpiresAt = expiresAt,
            GraceUntil = graceUntil,
            CustomerName = command.CustomerName,
            CustomerEmail = command.CustomerEmail,
            CustomerMsisdn = command.CustomerMsisdn,
            RedirectUrl = command.RedirectUrl,
            CallbackUrl = command.CallbackUrl,
            MetadataJson = command.MetadataJson,
        };

        var session = new PaymentSession
        {
            InvoiceId = invoice.Id,
            WalletId = wallet.Id,
            ExpectedAmount = chargedAmount,
            OpenedAt = now,
            ExpiresAt = expiresAt,
            State = SessionState.Open,
        };

        await store.SaveAsync(invoice, session, ct).ConfigureAwait(false);

        return new CreateInvoiceResult
        {
            Outcome = CreateInvoiceOutcome.Created,
            Invoice = invoice,
            Mode = mode,
        };
    }
}
