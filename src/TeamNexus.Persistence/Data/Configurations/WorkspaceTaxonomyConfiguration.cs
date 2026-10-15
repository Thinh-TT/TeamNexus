using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

/// <summary>
/// EF mapping for the fixed taxonomy dictionary (Giai đoạn 14.7 §A2): snake_case table, text+CHECK kind,
/// one unique key per (workspace, kind), RESTRICT FK, and the workspace soft-delete query filter.
/// </summary>
public sealed class WorkspaceTaxonomyConfiguration : IEntityTypeConfiguration<WorkspaceTaxonomy>
{
    public void Configure(EntityTypeBuilder<WorkspaceTaxonomy> builder)
    {
        // The kind is stored lowercase ('domain'/'work_type') unlike the other enums, matching the
        // member_type precedent (MemberTypeToStringConverter) and the CHECK below.
        builder.ToTable("workspace_taxonomies", table => table
            .HasCheckConstraint(
                "ck_workspace_taxonomies_kind",
                "\"kind\" IN ('domain', 'work_type')"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Kind)
            .HasConversion(
                kind => kind == TaxonomyKind.WorkType ? "work_type" : "domain",
                value => value == "work_type" ? TaxonomyKind.WorkType : TaxonomyKind.Domain)
            .HasMaxLength(20);

        builder.Property(x => x.Key)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Label)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(x => x.ColorHex)
            .HasMaxLength(10);

        // One key per kind per workspace. This is also the race guard that makes lazy seeding idempotent:
        // two concurrent first reads both try to insert the same keys, one loses with a unique violation and
        // re-reads instead of duplicating the dictionary.
        builder.HasIndex(x => new { x.WorkspaceId, x.Kind, x.Key })
            .IsUnique()
            .HasDatabaseName("uq_workspace_taxonomies_key");

        // The list query: entries of one kind, in display order.
        builder.HasIndex(x => new { x.WorkspaceId, x.Kind, x.Position })
            .HasDatabaseName("ix_workspace_taxonomies_lookup");

        builder.HasOne(x => x.Workspace)
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Keep consistent with Workspace's soft-delete filter (EF Core model validation): taxonomy entries
        // of a deleted workspace are invisible, exactly like its memberships and boards.
        builder.HasQueryFilter(x => x.Workspace == null || x.Workspace.DeletedAt == null);
    }
}
