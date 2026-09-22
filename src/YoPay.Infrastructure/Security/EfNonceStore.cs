using Microsoft.EntityFrameworkCore;
using YoPay.Application.Abstractions;
using YoPay.Domain.Entities;
using YoPay.Infrastructure.Persistence;

namespace YoPay.Infrastructure.Security;

/// <summary>
/// Nonce storage backed by the unique index on (key_id, nonce).
///
/// The insert is the check. Reading first and then writing would let two copies of the
/// same request, arriving in the same millisecond, both see the nonce free - which is
/// precisely the race a replay defence has to survive. Here the database decides, and
/// the loser of the race gets a unique violation, which is the answer rather than an
/// error.
/// </summary>
public sealed class EfNonceStore(YoPayDbContext db) : INonceStore
{
    /// <summary>PostgreSQL unique_violation.</summary>
    private const string UniqueViolation = "23505";

    public async Task<bool> TryConsumeAsync(
        string keyId,
        string nonce,
        DateTimeOffset expiresAt,
        CancellationToken ct = default)
    {
        db.RequestNonces.Add(new RequestNonce
        {
            KeyId = keyId,
            Nonce = nonce,
            ExpiresAt = expiresAt,
        });

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Seen before. Detach so the failed insert does not poison the next
            // SaveChanges on this context.
            foreach (var entry in db.ChangeTracker.Entries<RequestNonce>().ToList())
            {
                entry.State = EntityState.Detached;
            }

            return false;
        }
    }

    public Task<int> PruneExpiredAsync(DateTimeOffset asOf, CancellationToken ct = default) =>
        db.RequestNonces.Where(n => n.ExpiresAt < asOf).ExecuteDeleteAsync(ct);

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException?.GetType().GetProperty("SqlState")?.GetValue(ex.InnerException) as string
            == UniqueViolation;
}
