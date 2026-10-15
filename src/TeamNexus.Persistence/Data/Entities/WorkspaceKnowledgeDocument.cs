namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Lifecycle of a knowledge document (Giai đoạn 14.7 Track B §B3). Stored as lowercase text with a CHECK,
/// matching the <c>member_type</c>/<c>kind</c> precedent.
/// <para>
/// <see cref="Deleted"/> is a <b>soft delete expressed with the status column that already exists</b>:
/// the Accountability Layer requires an Undo for every write action, and a <c>deleted_at</c> column would
/// have meant a third migration for the phase. A re-created document is a new row, so a reused status
/// value is enough to make the old one invisible.
/// </para>
/// </summary>
public enum KnowledgeDocumentStatus
{
    /// <summary>Accepted, chunks written, embeddings not produced yet (or embeddings are switched off).</summary>
    Pending,

    /// <summary>Every chunk has an embedding; the document takes part in semantic retrieval.</summary>
    Ready,

    /// <summary>Embedding failed. Kept with an <c>error</c> so an operator can see what happened.</summary>
    Failed,

    /// <summary>Soft-deleted by an approved Undo. Never returned by the read paths.</summary>
    Deleted,
}

/// <summary>
/// One workspace document indexed for semantic retrieval by the board AI Agent Chat
/// (Giai đoạn 14.7 Track B §B3).
/// <para>
/// <b>Why a table and not the existing task data.</b> The agent can already read tasks and comments
/// (<c>SearchSystemData</c>); what it cannot reach is the knowledge that lives *beside* the board —
/// conventions, a spec, an external article. Those are free text with no structure to query, which is
/// exactly the case <c>pgvector</c> exists for.
/// </para>
/// <para>
/// <b>Creation goes through the Accountability Layer.</b> The table is written by
/// <c>CreateKnowledgeDocumentApplier</c> only after a Manager approves the <c>Pending</c> action, so a
/// read of this table can never contain something nobody decided to add.
/// </para>
/// </summary>
public class WorkspaceKnowledgeDocument : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    /// <summary>Human-facing document name, shown in the knowledge list and as the chunk's citation.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Where the content came from, when it was not typed into the UI. Null for manual paste.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>The whole document as submitted. Chunks are derived from it, never edited in place.</summary>
    public string Content { get; set; } = string.Empty;

    public KnowledgeDocumentStatus Status { get; set; } = KnowledgeDocumentStatus.Pending;

    /// <summary>Why embedding failed (capped). Null unless <see cref="Status"/> is <c>failed</c>.</summary>
    public string? Error { get; set; }

    /// <summary>How many chunks were written. Cached so the list view never has to count the child rows.</summary>
    public int ChunkCount { get; set; }

    /// <summary>The Manager who requested the document — the accountability trail, not the approver.</summary>
    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Workspace? Workspace { get; set; }

    public ApplicationUser? CreatedByUser { get; set; }

    public ICollection<WorkspaceKnowledgeChunk> Chunks { get; set; } = [];
}
