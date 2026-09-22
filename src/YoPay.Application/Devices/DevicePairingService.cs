using System.Security.Cryptography;
using YoPay.Application.Abstractions;
using YoPay.Domain.Entities;

namespace YoPay.Application.Devices;

public sealed class DevicePairingService(IDeviceStore devices, IClock clock)
{
    public async Task<PairResult> PairAsync(
        string token,
        string publicKeyBase64,
        string? model,
        string? appVersion,
        CancellationToken ct = default)
    {
        if (!IsUsableKey(publicKeyBase64))
        {
            return new PairResult(PairOutcome.InvalidPublicKey);
        }

        var pairing = await devices
            .FindUsableTokenAsync(PairingToken.Hash(token), ct)
            .ConfigureAwait(false);

        if (pairing is null || !pairing.IsUsable(clock.UtcNow))
        {
            return new PairResult(PairOutcome.TokenNotUsable);
        }

        var device = new Device
        {
            MerchantId = pairing.MerchantId,
            WalletId = pairing.WalletId,
            Fingerprint = Guid.CreateVersion7().ToString("N"),
            PublicKey = publicKeyBase64,
            Model = model,
            AppVersion = appVersion,
            Priority = 1,
            IsActive = true,
        };

        var paired = await devices.PairAsync(pairing, device, ct).ConfigureAwait(false);

        return paired is null
            ? new PairResult(PairOutcome.TokenNotUsable)
            : new PairResult(PairOutcome.Paired, paired.Id);
    }

    /// <summary>
    /// Rejects a key the server cannot verify with, at pairing time rather than at the
    /// first payment. A device that pairs successfully and then cannot be authenticated
    /// is the worst kind of failure: it looks connected on the dashboard and silently
    /// misses money.
    /// </summary>
    private static bool IsUsableKey(string publicKeyBase64)
    {
        if (string.IsNullOrWhiteSpace(publicKeyBase64))
        {
            return false;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);

            return ecdsa.KeySize == 256;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }
}
