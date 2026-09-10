using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

/// <summary>
/// EF mapping for AI Observer run records (Phase 5 §1 / DB design §3.6): snake_case table,
/// text+CHECK status, jsonb summary, RESTRICT FK to the workspace.
/// </summary>
public sealed class AiObserverRunConfiguration : IEntityTypeConfiguration<AiObserverRun>
{
    public void Configure(EntityTypeBuilder<AiObserverRun> builder)
    {
        builder.ToTable("ai_observer_runs", table => table
            .HasCheckConstraint(
                "ck_ai_observer_runs_status",
                "\"status\" IN ('Running', 'Completed', 'Skipped', 'Failed')"));

        builder.HasKey(x => x.Id);

        // Enum stored as text + CHECK (DB design §4). No DB default: the entity initialises the
        // value (same reasoning as AiActionLog.Status — Phase 4 §1.2).
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(x => x.Summary)
            .HasColumnType("jsonb");

        // No physical cascade anywhere (DB design §7): every FK is Restrict.
        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Run audit per workspace (DB design §5).
        builder.HasIndex(x => x.WorkspaceId);
    }
}
