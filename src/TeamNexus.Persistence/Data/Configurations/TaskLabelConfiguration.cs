using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

public sealed class TaskLabelConfiguration : IEntityTypeConfiguration<TaskLabel>
{
    public void Configure(EntityTypeBuilder<TaskLabel> builder)
    {
        builder.ToTable("task_labels");

        // A task can carry a given label only once.
        builder.HasKey(tl => new { tl.TaskId, tl.LabelId });

        builder.HasOne(tl => tl.Task)
            .WithMany()
            .HasForeignKey(tl => tl.TaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(tl => tl.Label)
            .WithMany()
            .HasForeignKey(tl => tl.LabelId)
            .OnDelete(DeleteBehavior.Restrict);

        // Junction rows follow their parents' soft-delete state: hidden when the task
        // is deleted, or when the label's workspace is deleted (matching the principal
        // query filters — EF Core model validation, pattern as in the other dependents).
        builder.HasQueryFilter(tl => (tl.Task == null || tl.Task.DeletedAt == null)
                                     && (tl.Label == null
                                         || tl.Label.Workspace == null
                                         || tl.Label.Workspace.DeletedAt == null));
    }
}
