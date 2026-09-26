using YoPay.Api.Auth;
using YoPay.Application.Invoicing;
using YoPay.Contracts.Api;
using YoPay.Domain.Enums;

namespace YoPay.Api.Endpoints;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this IEndpointRouteBuilder routes, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(configuration);

        var checkoutBaseUrl = configuration["Checkout:BaseUrl"]?.TrimEnd('/')
            ?? "https://localhost:5082";

        var group = routes.MapGroup("/v1/payment").RequireRateLimiting("merchant");

        group.MapPost("/create", async (
            CreateInvoiceRequest request,
            MerchantContext merchant,
            CreateInvoiceService service,
            CancellationToken ct) =>
        {
            if (!merchant.IsAuthenticated)
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.OrderRef))
            {
                return Results.Problem(
                    title: "orderRef is required",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.CreateAsync(
                new CreateInvoiceCommand
                {
                    MerchantId = merchant.MerchantId,
                    OrderRef = request.OrderRef,
                    Amount = request.Amount,
                    Method = request.Method,
                    WalletId = request.WalletId,
                    CustomerName = request.CustomerName,
                    CustomerEmail = request.CustomerEmail,
                    CustomerMsisdn = request.CustomerMsisdn,
                    RedirectUrl = request.RedirectUrl,
                    CallbackUrl = request.CallbackUrl,
                    MetadataJson = request.MetadataJson,
                },
                ct: ct).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                return Results.Problem(
                    title: result.Reason ?? "The invoice could not be created.",
                    statusCode: result.Outcome == CreateInvoiceOutcome.NoWallet
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status400BadRequest);
            }

            var invoice = result.Invoice!;

            // A repeated create answers 200 with the original invoice rather than 201.
            // The merchant's retry loop gets the same payment link it got the first time,
            // which is the whole point of making this idempotent.
            var response = new InvoiceResponse
            {
                InvoiceId = invoice.Id,
                OrderRef = invoice.OrderRef,
                Amount = invoice.Amount,
                ChargedAmount = invoice.ChargedAmount,
                Currency = invoice.Currency,
                Status = invoice.Status,
                ExpiresAt = invoice.ExpiresAt,
                CheckoutUrl = $"{checkoutBaseUrl}/pay/{invoice.Id}",
            };

            return result.Outcome == CreateInvoiceOutcome.Created
                ? Results.Created(response.CheckoutUrl!, response)
                : Results.Ok(response);
        });

        // Same answer as /verify, addressed the way a REST client expects. Both exist
        // because the SDKs call verify with whichever identifier they are holding - an
        // order reference from their own database, usually, not our invoice id.
        group.MapGet("/{invoiceId:guid}", async (
            Guid invoiceId,
            MerchantContext merchant,
            IInvoiceStore store,
            CancellationToken ct) =>
        {
            if (!merchant.IsAuthenticated)
            {
                return Results.Unauthorized();
            }

            var invoice = await store
                .FindByIdAsync(merchant.MerchantId, invoiceId, ct)
                .ConfigureAwait(false);

            // Not found rather than forbidden when it belongs to another merchant: the
            // lookup is scoped by merchant, so this answer cannot be used to discover
            // which invoice ids exist.
            if (invoice is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new InvoiceResponse
            {
                InvoiceId = invoice.Id,
                OrderRef = invoice.OrderRef,
                Amount = invoice.Amount,
                ChargedAmount = invoice.ChargedAmount,
                Currency = invoice.Currency,
                Status = invoice.Status,
                ExpiresAt = invoice.ExpiresAt,
                CheckoutUrl = $"{checkoutBaseUrl}/pay/{invoice.Id}",
            });
        });

        group.MapPost("/cancel", async (
            CancelInvoiceRequest request,
            MerchantContext merchant,
            CancelInvoiceService service,
            CancellationToken ct) =>
        {
            if (!merchant.IsAuthenticated)
            {
                return Results.Unauthorized();
            }

            var result = await service
                .CancelAsync(merchant.MerchantId, request.InvoiceId, request.OrderRef, ct)
                .ConfigureAwait(false);

            if (result.Outcome == CancelInvoiceOutcome.NotFound)
            {
                return Results.NotFound();
            }

            if (result.Outcome == CancelInvoiceOutcome.Refused)
            {
                // 409 rather than 400: the request was well formed, the invoice is simply
                // past the point where calling it off means anything.
                return Results.Problem(
                    title: result.Reason,
                    statusCode: StatusCodes.Status409Conflict);
            }

            var invoice = result.Invoice!;

            // Cancelling something already cancelled answers 200 as well. The caller is a
            // retrying HTTP client and the state it asked for is the state it has.
            return Results.Ok(new InvoiceResponse
            {
                InvoiceId = invoice.Id,
                OrderRef = invoice.OrderRef,
                Amount = invoice.Amount,
                ChargedAmount = invoice.ChargedAmount,
                Currency = invoice.Currency,
                Status = invoice.Status,
                ExpiresAt = invoice.ExpiresAt,
            });
        });

        group.MapPost("/verify", async (
            VerifyInvoiceRequest request,
            MerchantContext merchant,
            IInvoiceStore store,
            CancellationToken ct) =>
        {
            if (!merchant.IsAuthenticated)
            {
                return Results.Unauthorized();
            }

            var invoice = request.InvoiceId is { } id
                ? await store.FindByIdAsync(merchant.MerchantId, id, ct).ConfigureAwait(false)
                : await store.FindByOrderRefAsync(merchant.MerchantId, request.OrderRef ?? "", ct)
                    .ConfigureAwait(false);

            if (invoice is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new VerifyInvoiceResponse
            {
                InvoiceId = invoice.Id,
                Status = invoice.Status,
                ReceivedAmount = invoice.Status is InvoiceStatus.Paid or InvoiceStatus.Settled
                    ? invoice.ChargedAmount
                    : null,
                PaidAt = invoice.PaidAt,
            });
        });
    }
}
