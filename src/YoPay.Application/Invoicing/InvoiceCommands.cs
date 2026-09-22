using YoPay.Domain.Enums;

namespace YoPay.Application.Invoicing;

public sealed record CreateInvoiceCommand
{
    public required Guid MerchantId { get; init; }
    public required string OrderRef { get; init; }
    public required decimal Amount { get; init; }
    public required PaymentMethod Method { get; init; }
    public Guid? WalletId { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public string? CustomerMsisdn { get; init; }
    public string? RedirectUrl { get; init; }
    public string? CallbackUrl { get; init; }
    public string? MetadataJson { get; init; }
}

public enum CreateInvoiceOutcome
{
    Created = 0,
    /// <summary>The order reference already exists. The original invoice is returned
    /// unchanged - a retried create must never produce a second invoice.</summary>
    AlreadyExists = 1,
    NoWallet = 2,
    InvalidAmount = 3,
}

public sealed record CreateInvoiceResult
{
    public required CreateInvoiceOutcome Outcome { get; init; }
    public Domain.Entities.Invoice? Invoice { get; init; }
    public MatchingMode Mode { get; init; }
    public string? Reason { get; init; }

    public bool Succeeded => Outcome is CreateInvoiceOutcome.Created or CreateInvoiceOutcome.AlreadyExists;
}
