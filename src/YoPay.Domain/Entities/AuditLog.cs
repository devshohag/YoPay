using YoPay.Domain.Common;

namespace YoPay.Domain.Entities;

/// <summary>
/// Append-only record of anything a human did that touched money: a manual match, a
/// refund, a wallet change, a credential rotation.
/// </summary>
public class AuditLog : Entity
{
    public string Actor { get; set; } = null!;
    public string Action { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public Guid? EntityId { get; set; }
    public Guid? MerchantId { get; set; }

    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? Ip { get; set; }

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
