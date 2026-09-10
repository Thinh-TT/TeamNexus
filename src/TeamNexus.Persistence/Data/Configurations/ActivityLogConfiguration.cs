using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

/// <summary>
/// EF mapping for the AI Observer's activity event store (Phase 5 §1 / DB design §3.6):
/// snake_case table, jsonb payload, text+CHECK-free free-text action, RESTRICT FKs.
/// <para>
/// No query filter on purpose: this is an append-only event store, so rows stay readable even
/// after the referenced board/user/workspace is soft-deleted. Foreign keys are declared without
/// navigation properties (referenced via <c>HasOne&lt;T&gt;()</c>) so EF cannot inherit the
/// principal's soft-delete filter.
/// </para>
/// </summary>
public sealed class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.ToTable("activity_logs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EntityType)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Action)
            .HasMaxLength(64)
            .IsRequired();

        // String ↔ jsonb mapping (Npgsql "string mapping"): stored/loaded as raw JSON text and
        // (de)serialized explicitly in the Ai module — same convention as ai_action_logs (Phase 4 §0 D3).
        builder.Property(x => x.Payload)
            .HasColumnType("jsonb");

        // No physical cascade anywhere (DB design §7): every FK is Restrict.
        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Board>()
            .WithMany()
            .HasForeignKey(x => x.BoardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Observer scans the log per workspace in time order (DB design §5).
        builder.HasIndex(x => new { x.WorkspaceId, x.CreatedAt });
    }
}
