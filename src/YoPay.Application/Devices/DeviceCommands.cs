namespace YoPay.Application.Devices;

public enum PairOutcome
{
    Paired = 0,
    /// <summary>Unknown, already used, or past its five minutes. One answer for all
    /// three: saying which tells someone probing what a valid code looks like.</summary>
    TokenNotUsable = 1,
    InvalidPublicKey = 2,
}

public sealed record PairResult(PairOutcome Outcome, Guid? DeviceId = null)
{
    public bool Succeeded => Outcome == PairOutcome.Paired;
}

public sealed record IngestOutcome
{
    public required int Accepted { get; init; }
    public required int Duplicates { get; init; }
    public required int Rejected { get; init; }
}
