using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

/// <summary>
/// EF mapping for the AI Agent Chat transcript (Phase 14.5 §B): snake_case table, text+CHECK role,
/// jsonb attachments, RESTRICT FKs, and one CHECK enforcing that a turn belongs to a board
/// <b>or</b> a task but never both.
/// </summary>
public sealed class AgentChatMessageConfiguration : IEntityTypeConfiguration<AgentChatMessage>
{
    public void Configure(EntityTypeBuilder<AgentChatMessage> builder)
    {
        // Two `ToTable` calls, each with ONE lambda-based constraint.
        //
        // The chained form (`table.HasCheckConstraint(a).HasCheckConstraint(b)`) does not compile in EF
        // Core 10 — the lambda overload returns `CheckConstraintBuilder`, which has no second
        // `HasCheckConstraint` — so the constraints are declared one per call. EF merges both table
        // configurations, and the generated snapshot keeps both (verified against
        // `TeamNexusDbContextModelSnapshot`). The non-lambda `builder.HasCheckConstraint(...)` overload
        // would also work but is obsolete in this version.
        builder.ToTable("agent_chat_messages", table => table
            .HasCheckConstraint(
                "ck_agent_chat_messages_role",
                "\"role\" IN ('User', 'Assistant')"));

        // `num_nonnulls` is the direct expression of "one of these two, and only one" — two separate
        // `IS NOT NULL` checks could not express it without an OR/AND pair.
        builder.ToTable("agent_chat_messages", table => table
            .HasCheckConstraint(
                "ck_agent_chat_messages_scope",
                "num_nonnulls(\"board_id\", \"task_id\") = 1"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Role)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(x => x.Content)
            .IsRequired();

        // Same jsonb-as-string mapping as the Phase 4 snapshots: the provider stores/loads raw JSON and
        // the Ai module (de)serializes explicitly.
        builder.Property(x => x.Attachments)
            .HasColumnType("jsonb");

        // No physical cascade anywhere (DB design §7): every FK is Restrict.
        // FK-only relationships (no navigations) — see the entity's remarks for why.
        builder.HasOne<Board>()
            .WithMany()
            .HasForeignKey(x => x.BoardId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<BoardTask>()
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Transcript load: newest-first keyset page for one board (DB design §5).
        builder.HasIndex(x => new { x.BoardId, x.CreatedAt });

        // Retention sweep: prune by workspace, oldest first.
        builder.HasIndex(x => new { x.WorkspaceId, x.CreatedAt });
    }
}
