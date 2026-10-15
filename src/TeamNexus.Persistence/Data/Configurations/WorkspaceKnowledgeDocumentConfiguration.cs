using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

/// <summary>
/// EF mapping for indexed workspace knowledge (Giai đoạn 14.7 Track B §B3). The status is stored as
/// lowercase text + CHECK, matching the <c>member_type</c>/<c>kind</c> precedent, so the four lifecycle
/// values are readable in the database and a new one cannot be invented by a typo in C#.
/// </summary>
public sealed class WorkspaceKnowledgeDocumentConfiguration : IEntityTypeConfiguration<WorkspaceKnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<WorkspaceKnowledgeDocument> builder)
    {
        builder.ToTable("workspace_knowledge_documents", table => table
            .HasCheckConstraint(
                "ck_workspace_knowledge_documents_status",
                "\"status\" IN ('pending', 'ready', 'failed', 'deleted')"));

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Title)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(d => d.SourceUrl)
            .HasMaxLength(1000);

        builder.Property(d => d.Content)
            .IsRequired();

        builder.Property(d => d.Status)
            .HasConversion(
                status => status == KnowledgeDocumentStatus.Ready
                    ? "ready"
                    : status == KnowledgeDocumentStatus.Failed
                        ? "failed"
                        : status == KnowledgeDocumentStatus.Deleted
                            ? "deleted"
                            : "pending",
                value => value == "ready"
                    ? KnowledgeDocumentStatus.Ready
                    : value == "failed"
                        ? KnowledgeDocumentStatus.Failed
                        : value == "deleted"
                            ? KnowledgeDocumentStatus.Deleted
                            : KnowledgeDocumentStatus.Pending)
            .HasMaxLength(20);

        builder.Property(d => d.Error)
            .HasMaxLength(500);

        builder.Property(d => d.ChunkCount)
            .HasDefaultValue(0)
            .IsRequired();

        // The list query: one workspace, newest first. `status` is not part of the key because the list is
        // expected to show a document that is still embedding.
        builder.HasIndex(d => new { d.WorkspaceId, d.CreatedAt })
            .HasDatabaseName("ix_workspace_knowledge_documents_lookup");

        // RESTRICT, exactly like every other workspace-scoped table (workspace_members,
        // workspace_taxonomies): the application soft-deletes a workspace, so a physical cascade would never
        // fire and declaring one would only hide the fact that workspaces are not hard-deleted.
        builder.HasOne(d => d.Workspace)
            .WithMany()
            .HasForeignKey(d => d.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.CreatedByUser)
            .WithMany()
            .HasForeignKey(d => d.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chunks die with their document: a chunk whose parent is gone is unreachable (retrieval starts from
        // the document), so leaving orphan rows behind would be pure storage waste.
        builder.HasMany(d => d.Chunks)
            .WithOne(c => c.Document)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Same soft-delete filter as the rest of the schema: knowledge of a deleted workspace is invisible.
        builder.HasQueryFilter(d => d.Workspace == null || d.Workspace.DeletedAt == null);
    }
}
