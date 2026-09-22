namespace YoPay.Contracts.Ingest;

public sealed record PairDeviceRequest
{
    public required string Token { get; init; }

    /// <summary>Base64 SubjectPublicKeyInfo of an EC P-256 key generated on the handset.
    /// The private half stays in the Android Keystore and never leaves it.</summary>
    public required string PublicKey { get; init; }

    public string? Model { get; init; }
    public string? AppVersion { get; init; }
}

public sealed record PairDeviceResponse
{
    public required Guid DeviceId { get; init; }
    public required string WalletNumber { get; init; }
    public required string MerchantName { get; init; }
}
