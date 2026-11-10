using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

/// <summary>
/// Entity configuration for <see cref="WebhookDeliveryLog"/>.
/// Table: webhook_delivery_logs (Phase 15 §1.3).
/// </summary>
public sealed class WebhookDeliveryLogConfiguration : IEntityTypeConfiguration<WebhookDeliveryLog>
{
    public void Configure(EntityTypeBuilder<WebhookDeliveryLog> builder)
    {
        builder.ToTable("webhook_delivery_logs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DeliveryId)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(x => x.DeliveryId)
            .IsUnique();

        builder.Property(x => x.Provider)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.EventType)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ErrorMessage);
    }
}
