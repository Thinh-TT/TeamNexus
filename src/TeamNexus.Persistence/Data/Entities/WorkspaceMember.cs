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
/// Member kind: a real person or the workspace AI assistant (Phase 7 §2.1 / DB design §3.3).
/// Stored as lowercase text + CHECK <c>ck_workspace_members_member_type</c>.
/// </summary>
public enum MemberType
{
    Human,

    AiAgent,
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

    /// <summary>
    /// Discriminator between humans and the AI Agent (Phase 7). This is the ONLY reliable way to
    /// tell an agent row apart — never infer it from <see cref="AiAgentName"/> or the absence of a
    /// password hash. Partial unique index <c>uq_workspace_members_ai_agent</c> allows exactly one
    /// agent per workspace.
    /// </summary>
    public MemberType MemberType { get; set; } = MemberType.Human;

    /// <summary>Display name of the agent inside the workspace; null for human members.</summary>
    public string? AiAgentName { get; set; }

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;

    public Workspace? Workspace { get; set; }

    public ApplicationUser? User { get; set; }
}
