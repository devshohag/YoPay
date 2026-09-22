using Microsoft.EntityFrameworkCore;
using YoPay.Application.Abstractions;
using YoPay.Infrastructure.Persistence;

namespace YoPay.Infrastructure.Security;

public sealed class EfCredentialLookup(YoPayDbContext db, ISecretProtector protector, IClock clock)
    : ICredentialLookup
{
    public async Task<MerchantCredential?> FindAsync(string keyId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        var row = await db.ApiCredentials
            .AsNoTracking()
            .Where(c => c.KeyId == keyId)
            .Select(c => new
            {
                c.MerchantId,
                c.KeyId,
                c.HmacSecretEncrypted,
                c.RevokedAt,
                MerchantActive = db.Merchants.Any(m => m.Id == c.MerchantId && m.IsActive),
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (row is null || !row.MerchantActive || (row.RevokedAt is { } revoked && revoked <= now))
        {
            return null;
        }

        return new MerchantCredential
        {
            MerchantId = row.MerchantId,
            KeyId = row.KeyId,
            HmacSecret = protector.Unprotect(row.HmacSecretEncrypted),
        };
    }

    /// <summary>
    /// Stamps last-used at most once a minute per key.
    ///
    /// Unthrottled this is a write on every single API call, all of them landing on the
    /// same row for a busy merchant - a hot spot that buys nothing, since nobody needs
    /// second-level precision on "when was this key last seen".
    /// </summary>
    public async Task TouchAsync(string keyId, string? ip, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var cutoff = now - TimeSpan.FromMinutes(1);

        await db.ApiCredentials
            .Where(c => c.KeyId == keyId && (c.LastUsedAt == null || c.LastUsedAt < cutoff))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.LastUsedAt, now)
                    .SetProperty(c => c.LastUsedIp, ip),
                ct)
            .ConfigureAwait(false);
    }
}
