using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

/// <summary>
/// EF mapping for the embedded slices behind Workspace RAG (Giai đoạn 14.7 Track B §B3).
/// <para>
/// The two things worth reading here are the <b>fixed-dimension column</b> and the <b>HNSW index</b>:
/// the column is <c>vector(1536)</c> (not an unconstrained <c>vector</c>) so a wrong-dimension insert fails
/// in the database rather than quietly producing vectors that can never be compared, and the index is HNSW
/// with <c>vector_cosine_ops</c> because retrieval ranks with the cosine distance operator (<c>&lt;=&gt;</c>).
/// The operator class and the query operator must match: an index built for <c>vector_l2_ops</c> is simply
/// not used by a cosine <c>ORDER BY</c>.
/// </para>
/// </summary>
public sealed class WorkspaceKnowledgeChunkConfiguration : IEntityTypeConfiguration<WorkspaceKnowledgeChunk>
{
    /// <summary>Dimension of the stored vectors. Must equal <c>Embeddings:Dimensions</c> (guarded at startup).</summary>
    public const int EmbeddingDimension = 1_536;

    public void Configure(EntityTypeBuilder<WorkspaceKnowledgeChunk> builder)
    {
        builder.ToTable("workspace_knowledge_chunks");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Content)
            .IsRequired();

        // `vector(1536)`: the dimension is part of the column type, which is what makes a mismatch between
        // the configured embedding model and the schema a hard failure instead of a silent one.
        builder.Property(c => c.Embedding)
            .HasColumnType($"vector({EmbeddingDimension})");

        // One position per document. This is also the idempotency guard for a document created twice from
        // the same request: the second insert of chunk N loses instead of duplicating the text.
        builder.HasIndex(c => new { c.DocumentId, c.ChunkIndex })
            .IsUnique()
            .HasDatabaseName("uq_workspace_knowledge_chunks_position");

        // The retrieval index. HNSW (not ivfflat) because it needs no training pass and behaves well on a
        // table that grows one document at a time — which is exactly how this table is written.
        builder.HasIndex(c => c.Embedding)
            .HasDatabaseName("ix_workspace_knowledge_chunks_embedding")
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops");

        // DENORMALISED on purpose (see the entity remarks): retrieval is scoped to one workspace and must not
        // need a join to the parent document, so the workspace id is copied at insert time.
        builder.HasIndex(c => c.WorkspaceId)
            .HasDatabaseName("ix_workspace_knowledge_chunks_workspace");

        builder.HasOne(c => c.Document)
            .WithMany(d => d.Chunks)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        // <b>Required, not cosmetic.</b> The parent document carries a soft-delete query filter, and EF Core
        // warns (and can materialise inconsistent graphs) when a required navigation points at a filtered
        // entity with no matching filter on this side: without it, a chunk could be returned while its
        // document was invisible, and `Include(d => d.Chunks)` could disagree with the document filter.
        // The filter deliberately goes through the DOCUMENT rather than the denormalised `workspace_id` so
        // there is exactly one source of truth for visibility.
        builder.HasQueryFilter(c => c.Document == null || c.Document.Workspace == null
            || c.Document.Workspace.DeletedAt == null);
    }
}
