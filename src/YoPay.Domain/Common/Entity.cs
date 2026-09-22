namespace YoPay.Domain.Common;

/// <summary>
/// Base for every persisted entity. Version 7 GUIDs are time-ordered, so primary key
/// inserts stay sequential in Postgres instead of scattering across the index.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Base for entities owned by a single merchant.</summary>
public abstract class MerchantEntity : Entity
{
    public Guid MerchantId { get; set; }
}
