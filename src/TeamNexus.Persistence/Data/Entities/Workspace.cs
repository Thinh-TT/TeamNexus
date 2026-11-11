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

    /// <summary>
    /// Monotonically increasing sequence counter for workspace-scoped task keys (<c>TN-1</c>, <c>TN-2</c>, ...;
    /// Phase 15.1 §T15.1.1). Incremented atomically when a task is created.
    /// </summary>
    public long NextTaskNumber { get; set; } = 1;
}
