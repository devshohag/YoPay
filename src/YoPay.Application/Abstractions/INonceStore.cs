namespace YoPay.Application.Abstractions;

/// <summary>
/// Remembers the nonces that have been used, so each signature works exactly once.
///
/// The implementation must be atomic: a read followed by a write lets two copies of the
/// same request, arriving together, both find the nonce free. The database's unique index
/// on (key_id, nonce) is what makes it atomic - the insert that loses simply fails, and
/// that failure is the answer.
/// </summary>
public interface INonceStore
{
    /// <summary>
    /// Records the nonce and returns true, or returns false if it was already used.
    /// Never throws on a duplicate: a replay is an expected outcome, not an error.
    /// </summary>
    Task<bool> TryConsumeAsync(
        string keyId,
        string nonce,
        DateTimeOffset expiresAt,
        CancellationToken ct = default);

    /// <summary>Removes expired rows. Called by the scheduler; the table is otherwise
    /// unbounded and made entirely of values that stopped mattering minutes after they
    /// were written.</summary>
    Task<int> PruneExpiredAsync(DateTimeOffset asOf, CancellationToken ct = default);
}
