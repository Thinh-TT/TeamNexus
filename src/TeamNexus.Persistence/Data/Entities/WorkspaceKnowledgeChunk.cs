using Pgvector;

namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// One retrievable slice of a <see cref="WorkspaceKnowledgeDocument"/> plus its embedding
/// (Giai đoạn 14.7 Track B §B3).
/// <para>
/// <b>Why <see cref="WorkspaceId"/> is denormalised.</b> Every retrieval is scoped to one workspace, and
/// the cosine query runs on the embedded vector index — the narrowest possible row means the planner can
/// use the HNSW index and filter without a join. The value is copied from the parent document at insert
/// time and never changes.
/// </para>
/// <para>
/// <b>Why the dimension is fixed in the schema.</b> The column is <c>vector(1536)</c> rather than an
/// unconstrained <c>vector</c>: an unconstrained column pushes the dimension check into the application,
/// where changing the embedding model quietly inserts vectors nobody can compare. The cost is that a model
/// with different dimensions needs an <c>ALTER COLUMN … TYPE vector(N)</c> plus a re-embed — accepted
/// deliberately, and guarded at startup by <c>EmbeddingOptions</c>.
/// </para>
/// </summary>
public class WorkspaceKnowledgeChunk : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DocumentId { get; set; }

    /// <summary>Denormalised from the document so retrieval never joins (see the class remarks).</summary>
    public Guid WorkspaceId { get; set; }

    /// <summary>Position of this chunk inside its document, 0-based. Unique per document.</summary>
    public int ChunkIndex { get; set; }

    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// <c>vector(1536)</c>. Null while the chunk is still <c>pending</c> — which is also the exact
    /// predicate the embedding runner scans for, so "not embedded yet" needs no extra flag column.
    /// </summary>
    public Vector? Embedding { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public WorkspaceKnowledgeDocument? Document { get; set; }
}
