namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Role of a user inside a specific workspace (second authorization layer,
/// alongside the global Identity roles). Table: workspace_members (DB design §3.3).
/// </summary>
public enum WorkspaceRole
{
    Admin,
    Manager,
    Member,
}

/// <summary>
/// Junction: a user's membership + role in one workspace.
/// Composite PK (WorkspaceId, UserId) guarantees a single role per user/workspace.
/// </summary>
public class WorkspaceMember
{
    public Guid WorkspaceId { get; set; }

    public Guid UserId { get; set; }

    public WorkspaceRole Role { get; set; } = WorkspaceRole.Member;

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;

    public Workspace? Workspace { get; set; }

    public ApplicationUser? User { get; set; }
}
