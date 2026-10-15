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
/// <para>
/// <b>Giai đoạn 14.7 §A2</b> adds the four <i>capacity</i> fields below. They exist because Jev cannot
/// guess who should own a task from a display name: assignee dispatching needs a machine-readable skill set
/// and a real load figure. The same columns serve human members and AI Agent rows.
/// </para>
/// </summary>
public class WorkspaceMember
{
    public Guid WorkspaceId { get; set; }

    public Guid UserId { get; set; }

    public WorkspaceRole Role { get; set; } = WorkspaceRole.Member;

    /// <summary>
    /// Discriminator between humans and an AI Agent (Phase 7). This is the ONLY reliable way to
    /// tell an agent row apart — never infer it from <see cref="AiAgentName"/> or the absence of a
    /// password hash. Since Giai đoạn 14.7 Track B §B1 the partial unique index
    /// <c>uq_workspace_members_ai_persona</c> allows <b>one agent row per persona</b> per workspace.
    /// </summary>
    public MemberType MemberType { get; set; } = MemberType.Human;

    /// <summary>Display name of the agent inside the workspace; null for human members.</summary>
    public string? AiAgentName { get; set; }

    /// <summary>
    /// Giai đoạn 14.7 §A2 — canonical skill slugs (<c>csharp</c>, <c>postgresql</c>, <c>react</c>, …),
    /// stored as a jsonb array.
    /// <para>
    /// A <b>list, not a join table</b>, deliberately: skills are only ever read as a whole set when
    /// dispatching work, never queried relationally, and a set is exactly what the model needs to see.
    /// Validated on write (lowercase slug form, capped count) by the member service.
    /// </para>
    /// </summary>
    public List<string> Skills { get; set; } = [];

    /// <summary>
    /// Giai đoạn 14.7 §A2 — the member's primary role (<c>Tech Lead</c>, <c>Backend Dev</c>, …).
    /// Free text with a service-level whitelist rather than an enum: the vocabulary is presentation-level
    /// and a new role must not require a migration.
    /// </summary>
    public string PrimaryRole { get; set; } = "Member";

    /// <summary>
    /// Giai đoạn 14.7 §A2 — relative capacity (1.0 = a full-time contributor). A part-time member can
    /// therefore absorb proportionally less work before <see cref="MaxWipLimit"/> is reached.
    /// </summary>
    public double CapacityWeight { get; set; } = 1.0;

    /// <summary>
    /// Giai đoạn 14.7 §A2 — how many tasks this member may hold in progress at once. Dispatching treats
    /// reaching this as a negative signal; 0 means "no limit configured".
    /// </summary>
    public int MaxWipLimit { get; set; } = 5;

    /// <summary>
    /// Giai đoạn 14.7 Track B §B1 — which virtual co-worker this row is: <c>general</c>,
    /// <c>scrum_master</c>, <c>tech_lead</c> or <c>qa_security</c>.
    /// <para>
    /// <b>Null for humans</b> — enforced by <c>ck_workspace_members_persona_human</c>, so a human row can
    /// never accidentally satisfy an agent lookup. Agent rows created before this column existed were
    /// backfilled to <c>'general'</c> inside the migration, which is what keeps
    /// <c>FindAgentUserIdAsync</c> pointing at the same row it always did.
    /// </para>
    /// <para>
    /// The unique index uses <c>COALESCE(persona_key, 'general')</c> because PostgreSQL treats every
    /// <c>NULL</c> as distinct: a plain unique index on <c>(workspace_id, persona_key)</c> would happily
    /// accept unlimited <c>NULL</c> agent rows and silently lose the "one row per persona" guarantee.
    /// </para>
    /// </summary>
    public string? PersonaKey { get; set; }

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;

    public Workspace? Workspace { get; set; }

    public ApplicationUser? User { get; set; }
}
