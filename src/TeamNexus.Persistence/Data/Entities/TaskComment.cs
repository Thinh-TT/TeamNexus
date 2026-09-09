namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// A comment on a task — also a source of "communication context" for the AI Observer.
/// Table: task_comments (Phase 2 §1 / DB design §3.4).
/// </summary>
public class TaskComment : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TaskId { get; set; }

    public Guid AuthorId { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public BoardTask? Task { get; set; }

    public ApplicationUser? Author { get; set; }
}
