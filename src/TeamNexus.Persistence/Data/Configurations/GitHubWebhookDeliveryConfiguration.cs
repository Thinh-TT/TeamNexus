using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

public sealed class GitHubWebhookDeliveryConfiguration : IEntityTypeConfiguration<GitHubWebhookDelivery>
{
    public void Configure(EntityTypeBuilder<GitHubWebhookDelivery> builder)
    {
        builder.ToTable("github_webhook_deliveries", table => table
            .HasCheckConstraint(
                "ck_github_webhook_deliveries_status",
                "\"status\" IN ('Processed', 'Ignored', 'Failed')"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DeliveryId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.EventName)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Action)
            .HasMaxLength(64);

        builder.Property(x => x.RepositoryFullName)
            .HasMaxLength(256);

        builder.Property(x => x.Status)
            .HasMaxLength(32)
            .HasDefaultValue("Processed")
            .IsRequired();

        builder.Property(x => x.Summary)
            .HasMaxLength(500);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(1000);

        builder.HasIndex(x => new { x.IntegrationId, x.DeliveryId })
            .IsUnique()
            .HasDatabaseName("uq_github_webhook_deliveries_integration_delivery");

        builder.HasIndex(x => new { x.IntegrationId, x.ReceivedAt })
            .HasDatabaseName("ix_github_webhook_deliveries_integration_received");

        builder.HasOne(x => x.Integration)
            .WithMany(i => i.Deliveries)
            .HasForeignKey(x => x.IntegrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

