namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Enforces workspace-scoped uniqueness of sequential task numbers (<c>TN-1</c>, <c>TN-2</c>, ...;
/// Phase 15.1 §T15.1.2). Because <c>tasks</c> only stores <c>board_id</c> (not <c>workspace_id</c>),
/// PostgreSQL cannot enforce a composite unique constraint across a join; this companion table owns
/// <c>PRIMARY KEY (workspace_id, task_number)</c> and <c>UNIQUE (task_id)</c>.
/// </summary>
public class TaskNumberEntry
{
    public Guid WorkspaceId { get; set; }

    public long TaskNumber { get; set; }

    public Guid TaskId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

