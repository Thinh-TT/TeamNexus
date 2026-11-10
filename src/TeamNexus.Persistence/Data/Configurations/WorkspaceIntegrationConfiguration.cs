using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

/// <summary>
/// Entity configuration for <see cref="WorkspaceIntegration"/>.
/// Table: workspace_integrations (Phase 15 §1.1).
/// </summary>
public sealed class WorkspaceIntegrationConfiguration : IEntityTypeConfiguration<WorkspaceIntegration>
{
    public void Configure(EntityTypeBuilder<WorkspaceIntegration> builder)
    {
        builder.ToTable("workspace_integrations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Provider)
            .IsRequired();

        builder.Property(x => x.IsEnabled)
            .HasDefaultValue(true);

        builder.HasIndex(x => new { x.WorkspaceId, x.Provider })
            .IsUnique();

        builder.HasOne(x => x.Workspace)
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Soft delete consistency: hide integration when parent workspace is deleted
        builder.HasQueryFilter(x => x.Workspace == null || x.Workspace.DeletedAt == null);
    }
}
