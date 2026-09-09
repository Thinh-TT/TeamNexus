namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Task priority. Stored as text + CHECK constraint (DB design §4). Nullable on the task.
/// </summary>
public enum TaskPriority
{
    Low,
    Medium,
    High,
    Urgent,
}

/// <summary>
/// A Kanban task. Named BoardTask to avoid clashing with System.Threading.Tasks.Task.
/// Table: tasks (Phase 2 §1 / DB design §3.4).
/// BoardId mirrors column.BoardId (consistency enforced at the app layer) so tasks can be
/// grouped/broadcast per board and queried without joining the column.
/// </summary>
public class BoardTask : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Board this task belongs to (denormalized — must equal Column.BoardId).</summary>
    public Guid BoardId { get; set; }

    /// <summary>Column (status) the task currently sits in.</summary>
    public Guid ColumnId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Order within the column. Not unique — reordering may collide transiently (last-write-wins).</summary>
    public int Position { get; set; }

    public Guid? AssigneeId { get; set; }

    public DateTimeOffset? DueDate { get; set; }

    public TaskPriority? Priority { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Set when the task reaches a "done" column (moved to Done).</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Soft delete marker.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public Board? Board { get; set; }

    public BoardColumn? Column { get; set; }

    public ApplicationUser? Assignee { get; set; }

    public ApplicationUser? CreatedByUser { get; set; }
}
