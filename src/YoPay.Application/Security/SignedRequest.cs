namespace YoPay.Application.Security;

/// <summary>The four values a caller sends alongside a signed request.</summary>
public sealed record SignedRequest
{
    public const string KeyHeader = "X-YoPay-Key";
    public const string TimestampHeader = "X-YoPay-Timestamp";
    public const string NonceHeader = "X-YoPay-Nonce";
    public const string SignatureHeader = "X-YoPay-Signature";

    public required string KeyId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string Nonce { get; init; }
    public required string Signature { get; init; }

    public required string Method { get; init; }

    /// <summary>Path and query exactly as sent, e.g. /v1/payment/create?x=1.</summary>
    public required string PathAndQuery { get; init; }

    public required string Body { get; init; }
}

public enum SignatureFailure
{
    None = 0,
    MalformedHeaders = 1,
    /// <summary>Outside the accepted clock window.</summary>
    Expired = 2,
    /// <summary>The signature does not match.</summary>
    Invalid = 3,
    /// <summary>The nonce has been used before.</summary>
    Replayed = 4,
    UnknownKey = 5,
    RevokedKey = 6,
}

public sealed record SignatureResult(bool Succeeded, SignatureFailure Failure)
{
    public static readonly SignatureResult Ok = new(true, SignatureFailure.None);

    public static SignatureResult Fail(SignatureFailure failure) => new(false, failure);
}
