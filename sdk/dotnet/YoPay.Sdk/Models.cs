using System;
using System.Text.Json.Serialization;

namespace YoPay.Sdk;

/// <summary>
/// Plain classes rather than records, so this library also compiles for .NET Framework -
/// which is what a good share of the ASP.NET MVC shops in this market are still on, and
/// an SDK they cannot reference is an SDK they will not use.
/// </summary>
public sealed class CreateInvoiceRequest
{
    public string OrderRef { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = "Bkash";

    public Guid? WalletId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerMsisdn { get; set; }
    public string? RedirectUrl { get; set; }

    /// <summary>Anything you want handed back to you on the webhook, as a JSON string.</summary>
    public string? MetadataJson { get; set; }
}

public sealed class Invoice
{
    public Guid InvoiceId { get; set; }
    public string OrderRef { get; set; } = string.Empty;

    /// <summary>What you asked for.</summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// What the customer must actually send, and the only figure to show them.
    ///
    /// YoPay may adjust the amount by a few poisha so the incoming figure is unambiguous
    /// on that wallet. Display Amount instead and the customer sends the wrong number,
    /// which does not match and lands in the review queue.
    /// </summary>
    public decimal ChargedAmount { get; set; }

    public string Currency { get; set; } = "BDT";
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public string? CheckoutUrl { get; set; }

    public bool IsPaid => Status == InvoiceStatuses.Paid || Status == InvoiceStatuses.Settled;
}

public sealed class PaymentStatus
{
    public Guid InvoiceId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal? ReceivedAmount { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? TrxId { get; set; }

    public bool IsPaid => Status == InvoiceStatuses.Paid || Status == InvoiceStatuses.Settled;
}

public sealed class WebhookEndpoint
{
    public Guid EndpointId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? LastFailureReason { get; set; }

    /// <summary>Present only on the response that created the endpoint. Store it then.</summary>
    public string? Secret { get; set; }
}

public static class InvoiceStatuses
{
    public const string Created = "Created";
    public const string AwaitingPayment = "AwaitingPayment";
    public const string Partial = "Partial";

    /// <summary>
    /// The window elapsed with nothing matched. Not a failure: operator messages run
    /// minutes late and an offline handset uploads hours late, so an invoice waits here
    /// rather than failing outright. Do not cancel the order on this.
    /// </summary>
    public const string PendingReview = "PendingReview";

    public const string Paid = "Paid";
    public const string Settled = "Settled";
    public const string Expired = "Expired";
    public const string Cancelled = "Cancelled";
}

/// <summary>The body of a webhook delivery.</summary>
public sealed class WebhookPayload
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = string.Empty;

    public Guid InvoiceId { get; set; }
    public string OrderRef { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal? ReceivedAmount { get; set; }
    public string? TrxId { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public string? MetadataJson { get; set; }
}

public static class WebhookEvents
{
    public const string PaymentPaid = "payment.paid";
    public const string PaymentPartial = "payment.partial";
    public const string PaymentExpired = "payment.expired";
    public const string PaymentPendingReview = "payment.pending_review";
}
