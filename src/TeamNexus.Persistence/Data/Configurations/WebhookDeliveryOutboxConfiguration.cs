using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

public sealed class WebhookDeliveryOutboxConfiguration : IEntityTypeConfiguration<WebhookDeliveryOutbox>
{
    public void Configure(EntityTypeBuilder<WebhookDeliveryOutbox> builder)
    {
        builder.ToTable("webhook_delivery_outbox", table => table
            .HasCheckConstraint(
                "ck_webhook_delivery_outbox_status",
                "\"status\" IN ('Pending', 'Sent', 'Failed')"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EventType)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.DedupeKey)
            .HasMaxLength(200);

        builder.Property(x => x.PayloadJson)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasMaxLength(32)
            .HasDefaultValue("Pending")
            .IsRequired();

        builder.Property(x => x.AttemptCount)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.MaxAttempts)
            .HasDefaultValue(5)
            .IsRequired();

        builder.Property(x => x.LastError)
            .HasMaxLength(500);

        builder.HasIndex(x => new { x.Status, x.NextAttemptAt })
            .HasDatabaseName("ix_webhook_delivery_outbox_status_next_attempt");

        builder.HasIndex(x => new { x.EndpointId, x.CreatedAt })
            .HasDatabaseName("ix_webhook_delivery_outbox_endpoint_created");

        builder.HasOne(x => x.Endpoint)
            .WithMany(e => e.Deliveries)
            .HasForeignKey(x => x.EndpointId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

