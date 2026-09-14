using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

/// <summary>
/// EF mapping for the transactional-email audit log (Phase 11): snake_case table, status as
/// text + CHECK, RESTRICT FKs.
/// <para>
/// No query filter and no soft delete: the log is evidence that an email was handed to the
/// provider (and the counter behind the per-workspace rate limit), so it must survive the
/// soft-deletion of the workspace it was sent in the name of.
/// </para>
/// </summary>
public sealed class EmailMessageConfiguration : IEntityTypeConfiguration<EmailMessage>
{
    public void Configure(EntityTypeBuilder<EmailMessage> builder)
    {
        builder.ToTable("email_messages", table =>
            table.HasCheckConstraint(
                "ck_email_messages_status",
                "\"status\" IN ('Queued', 'Sent', 'Failed')"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ToEmail)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(x => x.Kind)
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.Subject)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.BodyPreview)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ProviderMessageId)
            .HasMaxLength(200);

        builder.Property(x => x.Error)
            .HasMaxLength(500);

        // Audit trail + the per-workspace "emails sent in the last hour" quota query.
        builder.HasIndex(x => new { x.WorkspaceId, x.CreatedAt });

        // No physical cascade anywhere (DB design §7): every FK is Restrict.
        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Declared without a navigation: the sender is audit data, not a relationship the API
        // walks — which also keeps the principal's filter out of this query path (Phase 5 pattern).
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.SentByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
