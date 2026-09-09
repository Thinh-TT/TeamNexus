namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// A workspace owns boards and members. Soft-deletable.
/// Table: workspaces (Phase 1 §2 / DB design §3.3).
/// </summary>
public class Workspace : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Guid OwnerId { get; set; }

    public ApplicationUser? Owner { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}
