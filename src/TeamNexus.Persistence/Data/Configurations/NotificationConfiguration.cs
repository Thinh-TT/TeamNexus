using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

/// <summary>
/// EF mapping for AI Observer alerts (Phase 5 §1 / DB design §3.6). One row per recipient
/// (Manager/Admin), so unread lookups only need <c>(recipient_user_id, is_read)</c>.
/// <para>
/// No query filter: alerts are immutable history (a Manager leaving the workspace does not
/// delete them); access is filtered by <c>recipient_user_id</c> at the service layer.
/// </para>
/// </summary>
public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Message)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(x => x.Payload)
            .HasColumnType("jsonb");

        // No physical cascade anywhere (DB design §7): every FK is Restrict.
        builder.HasOne(x => x.RecipientUser)
            .WithMany()
            .HasForeignKey(x => x.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Workspace is only ever an FK here (the API is "my notifications"), so no navigation
        // is exposed — which also keeps the Workspace soft-delete filter out of this query path.
        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unread alerts of one recipient (DB design §5).
        builder.HasIndex(x => new { x.RecipientUserId, x.IsRead });
    }
}
