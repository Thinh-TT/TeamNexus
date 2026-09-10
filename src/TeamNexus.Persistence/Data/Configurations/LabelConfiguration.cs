using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

public sealed class LabelConfiguration : IEntityTypeConfiguration<Label>
{
    public void Configure(EntityTypeBuilder<Label> builder)
    {
        builder.ToTable("labels");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Name)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(l => l.Color)
            .HasMaxLength(9) // #RRGGBB (7) or #RRGGBBAA (9)
            .IsRequired();

        // Label names must be unique within a workspace (DB design §5).
        builder.HasIndex(l => new { l.WorkspaceId, l.Name })
            .IsUnique();

        builder.HasOne(l => l.Workspace)
            .WithMany()
            .HasForeignKey(l => l.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Visible only while its workspace is not deleted (keeps the principal query
        // filter consistent — same pattern as WorkspaceMember / BoardColumn).
        builder.HasQueryFilter(l => l.Workspace == null || l.Workspace.DeletedAt == null);
    }
}
