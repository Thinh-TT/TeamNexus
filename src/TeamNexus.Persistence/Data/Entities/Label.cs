namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// A task label, scoped to a workspace. Attached to tasks through the TaskLabel junction.
/// Table: labels (Phase 2 §1 / DB design §3.4). Only CreatedAt — labels are not edited.
/// </summary>
public class Label
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    /// <summary>Label name, unique within the workspace (UQ (WorkspaceId, Name)).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Hex color, e.g. #3B82F6.</summary>
    public string Color { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Workspace? Workspace { get; set; }
}
