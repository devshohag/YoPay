namespace YoPay.Application.Invoicing;

public enum SubmitClaimOutcome
{
    Accepted = 0,

    /// <summary>The same id was already submitted for this invoice. Answered as success:
    /// a customer pressing the button twice has not done anything wrong.</summary>
    AlreadySubmitted = 1,

    /// <summary>Nothing resembling a transaction id in what was typed.</summary>
    Malformed = 2,

    InvoiceNotFound = 3,

    /// <summary>The invoice is already paid, cancelled or long past its grace period.</summary>
    NotAcceptingClaims = 4,

    /// <summary>Too many attempts on this invoice.</summary>
    TooManyAttempts = 5,
}

public sealed record SubmitClaimResult(SubmitClaimOutcome Outcome, string? NormalisedTrxId = null)
{
    public bool Succeeded =>
        Outcome is SubmitClaimOutcome.Accepted or SubmitClaimOutcome.AlreadySubmitted;
}
