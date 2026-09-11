using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

/// <summary>
/// EF mapping for the AI Agent run journal (Phase 7 §2.3 / DB design §3.8): snake_case table,
/// text+CHECK enums, jsonb tool trace, RESTRICT FKs, no soft delete and no query filter.
/// </summary>
public sealed class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        builder.ToTable("agent_runs", table =>
        {
            table.HasCheckConstraint(
                "ck_agent_runs_status",
                "\"status\" IN ('Running', 'AwaitingClarification', 'AwaitingApproval', 'Completed', 'Failed')");

            table.HasCheckConstraint(
                "ck_agent_runs_stop_reason",
                "\"stop_reason\" IN ('DraftProduced', 'QuestionAsked', 'ToolLimit', 'TimeLimit', "
                + "'TokenBudget', 'ProviderError', 'Cancelled', 'TaskChanged', 'InternalError')");

            table.HasCheckConstraint(
                "ck_agent_runs_output_kind",
                "\"output_kind\" IS NULL OR \"output_kind\" IN ('Comment', 'Attachment')");
        });

        builder.HasKey(x => x.Id);

        // Enums stored as text + CHECK (DB design §4). No DB default: the entity initialises the
        // value (same reasoning as AiObserverRun.Status — Phase 5 §1).
        // Length 32 is required, not cosmetic: the longest status is "AwaitingClarification"
        // (21 chars) — at 16 every run parked on a clarification question would fail to insert.
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.StopReason)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.OutputKind)
            .HasMaxLength(32);

        builder.Property(x => x.ClarificationQuestion)
            .HasMaxLength(2000);

        builder.Property(x => x.Error)
            .HasMaxLength(2000);

        // jsonb keeps the raw JSON text (Npgsql string mapping) — the Ai module serializes the
        // compact trace explicitly, exactly like AiActionLog.Basis (Phase 4 §0 — D3).
        builder.Property(x => x.ToolCallTrace)
            .HasColumnType("jsonb")
            .IsRequired();

        // No physical cascade anywhere (DB design §7): every FK is Restrict. Relationships are
        // declared without navigations so no principal query filter leaks into run lookups.
        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Board>()
            .WithMany()
            .HasForeignKey(x => x.BoardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BoardTask>()
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.AgentUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.TriggeredByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Comments are soft-deleted, but agent_runs has no query filter, so these optional FKs stay
        // readable (use IgnoreQueryFilters when the clarification question must be re-read later).
        builder.HasOne<TaskComment>()
            .WithMany()
            .HasForeignKey(x => x.ClarificationCommentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TaskComment>()
            .WithMany()
            .HasForeignKey(x => x.ResolutionCommentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Append-only chain: "Chạy lại" points at the run it continues (D14).
        builder.HasOne<AgentRun>()
            .WithMany()
            .HasForeignKey(x => x.PreviousRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AiActionLog>()
            .WithMany()
            .HasForeignKey(x => x.AiActionLogId)
            .OnDelete(DeleteBehavior.Restrict);

        // Run history of one task — powers the "Chạy lại" list (DB design §5).
        builder.HasIndex(x => new { x.TaskId, x.StartedAt });

        // Audit (and future optional retention pruning) per workspace (DB design §5).
        builder.HasIndex(x => new { x.WorkspaceId, x.StartedAt });

        // The orphan-run reaper only ever looks for Running rows (D13), so index them partially.
        builder.HasIndex(x => x.Status)
            .HasFilter("\"status\" = 'Running'");
    }
}
