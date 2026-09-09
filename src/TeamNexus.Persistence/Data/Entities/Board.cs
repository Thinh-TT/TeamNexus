namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Kanban board inside a workspace. Soft-deletable.
/// Table: boards (Phase 1 §2 creates the table; columns/tasks arrive in Phase 2 / DB design §3.4).
/// </summary>
public class Board : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Workspace? Workspace { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}
