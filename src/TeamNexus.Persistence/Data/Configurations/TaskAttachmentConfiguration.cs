using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

/// <summary>
/// EF mapping for AI Agent output files (Phase 7 §2.4 / DB design §3.8): snake_case table,
/// <c>bytea</c> content, RESTRICT FKs, NO soft delete and NO <c>updated_at</c> (D5).
/// </summary>
public sealed class TaskAttachmentConfiguration : IEntityTypeConfiguration<TaskAttachment>
{
    public void Configure(EntityTypeBuilder<TaskAttachment> builder)
    {
        builder.ToTable("task_attachments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FileName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.ContentType)
            .HasMaxLength(128)
            .IsRequired();

        // Inline bytes — this is the only binary column of the schema (DB design §1).
        // The 512 KB cap is enforced in the app layer (Agent:MaxAttachmentBytes), not by the DB.
        builder.Property(x => x.Content)
            .HasColumnType("bytea")
            .IsRequired();

        // No physical cascade anywhere (DB design §7): every FK is Restrict.
        builder.HasOne<BoardTask>()
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Traceability: which run produced this file (null for future non-agent uploads).
        builder.HasOne<AgentRun>()
            .WithMany()
            .HasForeignKey(x => x.SourceRunId)
            .OnDelete(DeleteBehavior.Restrict);

        // Attachments of one task (DB design §5).
        builder.HasIndex(x => x.TaskId);
    }
}
