using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

public sealed class WorkspaceWebhookEndpointConfiguration : IEntityTypeConfiguration<WorkspaceWebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WorkspaceWebhookEndpoint> builder)
    {
        builder.ToTable("workspace_webhook_endpoints", table => table
            .HasCheckConstraint(
                "ck_workspace_webhook_endpoints_provider",
                "\"provider\" IN ('slack', 'discord')"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(x => x.Provider)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.TargetUrl)
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(x => x.SubscribedEvents)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.FailureCount)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.LastError)
            .HasMaxLength(500);

        builder.HasIndex(x => new { x.WorkspaceId, x.IsActive })
            .HasDatabaseName("ix_workspace_webhook_endpoints_workspace_active");

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

