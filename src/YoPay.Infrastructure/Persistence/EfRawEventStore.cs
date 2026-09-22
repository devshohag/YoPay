using Microsoft.EntityFrameworkCore;
using YoPay.Application.Ingestion;
using YoPay.Domain.Entities;

namespace YoPay.Infrastructure.Persistence;

/// <summary>
/// The unique index on dedupe_hash is the deduplication. Checking first and inserting
/// second would let two copies of one upload, arriving together, both find the hash
/// absent - which is exactly the race at-least-once delivery guarantees will happen.
/// </summary>
public sealed class EfRawEventStore(YoPayDbContext db) : IRawEventStore
{
    private const string UniqueViolation = "23505";

    public async Task<bool> AddIfNewAsync(RawEvent rawEvent, CancellationToken ct = default)
    {
        db.RawEvents.Add(rawEvent);

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.Entry(rawEvent).State = EntityState.Detached;
            return false;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException?.GetType().GetProperty("SqlState")?.GetValue(ex.InnerException) as string
            == UniqueViolation;
}
