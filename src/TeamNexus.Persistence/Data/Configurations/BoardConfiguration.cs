using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

public sealed class BoardConfiguration : IEntityTypeConfiguration<Board>
{
    public void Configure(EntityTypeBuilder<Board> builder)
    {
        builder.ToTable("boards");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Name)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(b => b.Description)
            .HasMaxLength(2000);

        builder.HasOne(b => b.Workspace)
            .WithMany()
            .HasForeignKey(b => b.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Soft delete: hidden when the board OR its workspace is deleted (keeps the
        // principal query filter consistent – EF Core model validation).
        builder.HasQueryFilter(b => b.DeletedAt == null
                                    && (b.Workspace == null || b.Workspace.DeletedAt == null));
    }
}
