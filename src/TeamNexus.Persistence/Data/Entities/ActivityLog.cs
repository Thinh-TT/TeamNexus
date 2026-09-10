namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Append-only event store for board activity, feeding the AI Observer's periodic scan
/// (Phase 5 §1 / DB design §3.6). Table: <c>activity_logs</c>.
/// <para>
/// Deliberately <b>no</b> soft delete and <b>no</b> global query filter: the log must stay
/// readable even after the board/workspace it refers to is soft-deleted (the Observer and the
/// reporting phase read it as raw history). <see cref="UpdatedAt"/> exists only because the
/// entity follows the shared <see cref="IAuditableEntity"/> convention — for an append-only row
/// it carries no information.
/// </para>
/// </summary>
public class ActivityLog : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Workspace the event belongs to (Observer scans per workspace).</summary>
    public Guid WorkspaceId { get; set; }

    /// <summary>Board involved in the event; null for workspace-level events.</summary>
    public Guid? BoardId { get; set; }

    /// <summary>Actor that caused the event; null when the system produced it.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Affected entity type from <c>ObserverEntityTypes</c> (module Board), e.g. <c>Task</c>.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Id of the affected entity.</summary>
    public Guid? EntityId { get; set; }

    /// <summary>Event name from <c>ObserverActivityActions</c> (free text — DB design §4), e.g. <c>TaskMoved</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// jsonb — short event diff (column/assignee/priority changes); never a full text dump
    /// (token/cost control, DB design §7).
    /// </summary>
    public string? Payload { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
