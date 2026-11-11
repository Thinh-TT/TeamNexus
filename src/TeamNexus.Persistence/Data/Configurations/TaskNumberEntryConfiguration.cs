using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

public sealed class TaskNumberEntryConfiguration : IEntityTypeConfiguration<TaskNumberEntry>
{
    public void Configure(EntityTypeBuilder<TaskNumberEntry> builder)
    {
        builder.ToTable("task_numbers");

        builder.HasKey(x => new { x.WorkspaceId, x.TaskNumber });

        builder.HasIndex(x => x.TaskId)
            .IsUnique()
            .HasDatabaseName("uq_task_numbers_task_id");

        // FK-only relationships (no navigations) so soft-deleted tasks keep their reserved numbers
        // without triggering EF Core query-filter interaction warnings (Pitfall T6).
        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BoardTask>()
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

