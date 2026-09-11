namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// A file produced by the AI Agent as a long result of a task (Phase 7 §2.4 / DB design §3.8).
/// Table: <c>task_attachments</c>.
/// <para>
/// <b>This is the ONLY non-soft-deleted table of the schema (D5).</b> A row exists only after a
/// human approved the corresponding <c>ai_action_logs</c> entry; "Undone" physically deletes it.
/// Reason: <c>bytea</c> left behind burns real free-tier quota, and Undo must return the workspace
/// to the exact previous state. Accepted consequence: an undone file cannot be restored.
/// </para>
/// <para>
/// Deliberately NOT an <see cref="IAuditableEntity"/> — a generated file is immutable, so
/// <c>updated_at</c> would be meaningless. Also no soft delete and no query filter.
/// </para>
/// </summary>
public class TaskAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TaskId { get; set; }

    /// <summary>Author of the file — always the workspace's AI Agent user in this phase.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>The agent run that produced the file (null for future non-agent uploads).</summary>
    public Guid? SourceRunId { get; set; }

    /// <summary>ASCII-safe file name (reuses <c>ReportFileName.Slugify</c> — Phase 6).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary><c>text/markdown</c>, <c>text/plain</c>, <c>text/csv</c>, …</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Always <c>Content.Length</c>; the app layer rejects anything above <c>Agent:MaxAttachmentBytes</c>.</summary>
    public int SizeBytes { get; set; }

    /// <summary>File bytes, stored inline as PostgreSQL <c>bytea</c> (cap 512 KB by config).</summary>
    public byte[] Content { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
}
