using YoPay.Domain.Entities;

namespace YoPay.Application.Devices;

public sealed record DeviceIdentity
{
    public required Guid DeviceId { get; init; }
    public required Guid MerchantId { get; init; }
    public required Guid WalletId { get; init; }
    public required string PublicKey { get; init; }
    public required bool IsActive { get; init; }
}

public interface IDeviceStore
{
    Task<DeviceIdentity?> FindIdentityAsync(Guid deviceId, CancellationToken ct = default);

    Task<DevicePairingToken?> FindUsableTokenAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Creates the device and marks the token used, in one transaction. Returns
    /// null if the token was consumed by another request in the meantime.</summary>
    Task<Device?> PairAsync(
        DevicePairingToken token, Device device, CancellationToken ct = default);

    Task RecordHeartbeatAsync(
        Guid deviceId,
        string appVersion,
        Domain.Enums.DevicePermissionState permissionState,
        int? batteryPercent,
        string? networkType,
        CancellationToken ct = default);
}
