using YoPay.Domain.Common;

namespace YoPay.Domain.Entities;

/// <summary>
/// One-shot value accompanying a signed request.
///
/// A timestamp window alone does not stop replay: inside those five minutes the exact
/// same signed request can be sent again and again. The unique index on
/// (key_id, nonce) is what makes a signature usable exactly once.
///
/// Rows are pruned by the scheduler once ExpiresAt has passed.
/// </summary>
public class RequestNonce : Entity
{
    public string KeyId { get; set; } = null!;
    public string Nonce { get; set; } = null!;
    public DateTimeOffset SeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
}
