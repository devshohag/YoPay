using Microsoft.EntityFrameworkCore;
using YoPay.Application.Devices;
using YoPay.Domain.Entities;
using YoPay.Domain.Enums;

namespace YoPay.Infrastructure.Persistence;

public sealed class EfDeviceStore(YoPayDbContext db) : IDeviceStore
{
    public Task<DeviceIdentity?> FindIdentityAsync(Guid deviceId, CancellationToken ct = default) =>
        db.Devices
            .AsNoTracking()
            .Where(d => d.Id == deviceId && d.PublicKey != null)
            .Select(d => new DeviceIdentity
            {
                DeviceId = d.Id,
                MerchantId = d.MerchantId,
                WalletId = d.WalletId,
                PublicKey = d.PublicKey!,
                IsActive = d.IsActive,
            })
            .FirstOrDefaultAsync(ct);

    public Task<DevicePairingToken?> FindUsableTokenAsync(
        string tokenHash, CancellationToken ct = default) =>
        db.DevicePairingTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.ConsumedAt == null, ct);

    /// <summary>
    /// Creates the device and burns the token together.
    ///
    /// The update is conditional on the token still being unconsumed, so if two requests
    /// race, the second one updates zero rows and gets null back rather than creating a
    /// second device on someone else's wallet.
    /// </summary>
    public async Task<Device?> PairAsync(
        DevicePairingToken token, Device device, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(device);

        // The connection retries on a transient failure, and a retrying execution strategy
        // refuses a transaction opened behind its back. Hand it the whole unit instead.
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy
            .ExecuteAsync(() => PairInTransactionAsync(token, device, ct))
            .ConfigureAwait(false);
    }

    private async Task<Device?> PairInTransactionAsync(
        DevicePairingToken token, Device device, CancellationToken ct)
    {
        // A retry starts here again, so nothing from the failed attempt may survive.
        db.ChangeTracker.Clear();

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        var burned = await db.DevicePairingTokens
            .Where(t => t.Id == token.Id && t.ConsumedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.ConsumedAt, DateTimeOffset.UtcNow)
                    .SetProperty(t => t.DeviceId, device.Id),
                ct)
            .ConfigureAwait(false);

        if (burned == 0)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return null;
        }

        db.Devices.Add(device);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);

        return device;
    }

    public async Task RecordHeartbeatAsync(
        Guid deviceId,
        string appVersion,
        DevicePermissionState permissionState,
        int? batteryPercent,
        string? networkType,
        CancellationToken ct = default)
    {
        await db.Devices
            .Where(d => d.Id == deviceId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(d => d.LastHeartbeatAt, DateTimeOffset.UtcNow)
                    .SetProperty(d => d.AppVersion, appVersion)
                    .SetProperty(d => d.PermissionState, permissionState)
                    .SetProperty(d => d.BatteryPercent, batteryPercent)
                    .SetProperty(d => d.NetworkType, networkType),
                ct)
            .ConfigureAwait(false);
    }
}
