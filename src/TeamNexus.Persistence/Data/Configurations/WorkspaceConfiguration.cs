using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

public sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("workspaces");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Name)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(w => w.Description)
            .HasMaxLength(2000);

        builder.HasOne(w => w.Owner)
            .WithMany()
            .HasForeignKey(w => w.OwnerId)
            .OnDelete(DeleteBehavior.Restrict); // no physical cascade (DB design §7)

        // Soft delete: deleted rows are invisible to every query.
        builder.HasQueryFilter(w => w.DeletedAt == null);
    }
}
