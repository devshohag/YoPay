using YoPay.Domain.Entities;

namespace YoPay.Application.Ingestion;

public interface IRawEventStore
{
    /// <summary>
    /// Stores the event, or returns false if its hash is already present. Never throws on
    /// a duplicate: devices upload at least once by design, so a repeat is the expected
    /// case and not an error.
    /// </summary>
    Task<bool> AddIfNewAsync(RawEvent rawEvent, CancellationToken ct = default);
}
