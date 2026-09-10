using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

/// <summary>
/// EF mapping for the Accountability Layer log (Phase 4 §1 / DB design §3.5):
/// snake_case table, jsonb snapshots, text+CHECK status, RESTRICT FKs to users.
/// </summary>
public sealed class AiActionLogConfiguration : IEntityTypeConfiguration<AiActionLog>
{
    public void Configure(EntityTypeBuilder<AiActionLog> builder)
    {
        builder.ToTable("ai_action_logs", table => table
            .HasCheckConstraint(
                "ck_ai_action_logs_status",
                "\"status\" IN ('Pending', 'Approved', 'Rejected', 'Undone')"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Action)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.EntityType)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.DecisionNote)
            .HasMaxLength(500);

        // String ↔ jsonb mapping (Npgsql EF provider "string mapping"): the provider stores and
        // loads JSON columns without any further serialization, so snapshots stay as raw JSON
        // text and are (de)serialized explicitly in the Ai module (Phase 4 §0 — D3).
        builder.Property(x => x.Basis)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.BeforeSnapshot).HasColumnType("jsonb");
        builder.Property(x => x.AfterSnapshot).HasColumnType("jsonb");
        builder.Property(x => x.AppliedSnapshot).HasColumnType("jsonb");

        builder.Property(x => x.Status)
            .HasConversion<string>()   // enum stored as text + CHECK (DB design §4)
            .HasMaxLength(16);

        // Board-scoped action history: entity_type = 'Board' + entity_id = boardId (DB design §5).
        // The index on requested_by_user_id (also in §5) is added by EF's FK index convention.
        builder.HasIndex(x => new { x.EntityType, x.EntityId, x.CreatedAt });

        // No physical cascade anywhere (DB design §7): every FK is Restrict.
        builder.HasOne(x => x.RequestedByUser)
            .WithMany()
            .HasForeignKey(x => x.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.DecidedByUser)
            .WithMany()
            .HasForeignKey(x => x.DecidedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
