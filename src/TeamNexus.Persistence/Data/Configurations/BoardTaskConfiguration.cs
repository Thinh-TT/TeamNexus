using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

public sealed class BoardTaskConfiguration : IEntityTypeConfiguration<BoardTask>
{
    public void Configure(EntityTypeBuilder<BoardTask> builder)
    {
        // Table is "tasks" (DB design §3.4), entity class is BoardTask.
        builder.ToTable("tasks", table => table
            .HasCheckConstraint("ck_tasks_priority", "\"priority\" IN ('Low', 'Medium', 'High', 'Urgent')"));

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.Description)
            .HasMaxLength(2000);

        // Nullable enum stored as text + CHECK (DB design §4). HasConversion<string>()
        // handles Nullable<TaskPriority> by composing the built-in enum-to-string
        // converter with a null-preserving wrapper.
        builder.Property(t => t.Priority)
            .HasConversion<string>()
            .HasMaxLength(32);

        // Ordering index for drag & drop within a column (Phase 2 §1 / DB design §5).
        builder.HasIndex(t => new { t.BoardId, t.ColumnId, t.Position });

        // No physical cascade anywhere (DB design §7): every FK is Restrict.
        builder.HasOne(t => t.Board)
            .WithMany()
            .HasForeignKey(t => t.BoardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Column)
            .WithMany()
            .HasForeignKey(t => t.ColumnId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Assignee)
            .WithMany()
            .HasForeignKey(t => t.AssigneeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.CreatedByUser)
            .WithMany()
            .HasForeignKey(t => t.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // Soft delete: hidden when the task OR its board is deleted (keeps principal
        // query filters consistent — EF Core model validation, same as Board's filter).
        builder.HasQueryFilter(t => t.DeletedAt == null
                                    && (t.Board == null || t.Board.DeletedAt == null));
    }
}
