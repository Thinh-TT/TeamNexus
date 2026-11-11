using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

public sealed class TaskExternalLinkConfiguration : IEntityTypeConfiguration<TaskExternalLink>
{
    public void Configure(EntityTypeBuilder<TaskExternalLink> builder)
    {
        builder.ToTable("task_external_links", table => table
            .HasCheckConstraint(
                "ck_task_external_links_provider",
                "\"provider\" IN ('github')"));

        builder.ToTable("task_external_links", table => table
            .HasCheckConstraint(
                "ck_task_external_links_link_type",
                "\"link_type\" IN ('pull_request', 'issue')"));

        builder.ToTable("task_external_links", table => table
            .HasCheckConstraint(
                "ck_task_external_links_state",
                "\"state\" IN ('draft', 'open', 'merged', 'closed')"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Provider)
            .HasMaxLength(32)
            .HasDefaultValue("github")
            .IsRequired();

        builder.Property(x => x.LinkType)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ExternalId)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.ExternalUrl)
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(x => x.State)
            .HasMaxLength(32)
            .HasDefaultValue("open")
            .IsRequired();

        builder.HasIndex(x => new { x.TaskId, x.Provider, x.LinkType, x.ExternalId })
            .IsUnique()
            .HasDatabaseName("uq_task_external_links_task_external");

        builder.HasIndex(x => new { x.WorkspaceId, x.Provider, x.LinkType, x.ExternalId })
            .HasDatabaseName("ix_task_external_links_workspace_lookup");

        builder.HasOne(x => x.Task)
            .WithMany(t => t.ExternalLinks)
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Matching query filter for BoardTask (Pitfall T6 — prevents EF Core warning 10622).
        builder.HasQueryFilter(x => x.Task == null
            || (x.Task.DeletedAt == null && (x.Task.Board == null || x.Task.Board.DeletedAt == null)));
    }
}

