using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

public sealed class WorkspaceMemberConfiguration : IEntityTypeConfiguration<WorkspaceMember>
{
    public void Configure(EntityTypeBuilder<WorkspaceMember> builder)
    {
        builder.ToTable("workspace_members", table =>
        {
            table.HasCheckConstraint(
                "ck_workspace_members_role",
                "\"role\" IN ('Admin', 'Manager', 'Member')");

            // Phase 7 §2.1: the member kind is a lowercase text discriminator (DB design §4).
            table.HasCheckConstraint(
                "ck_workspace_members_member_type",
                "\"member_type\" IN ('human', 'ai_agent')");

            // Giai đoạn 14.7 §A2. Capacity is a multiplier: zero or negative would make a member absorb
            // infinite work in the dispatching maths, so the database refuses it rather than relying on
            // every reader remembering to clamp.
            table.HasCheckConstraint(
                "ck_workspace_members_capacity_weight",
                "\"capacity_weight\" > 0");
        });

        // A negative WIP limit is meaningless ("may hold minus two tasks"), so it is rejected at the source.
        // Declared separately because the chained lambda form does not compile in EF Core 10 (the
        // AgentChatMessageConfiguration precedent) — EF merges both table configurations.
        builder.ToTable("workspace_members", table => table
            .HasCheckConstraint(
                "ck_workspace_members_max_wip_limit",
                "\"max_wip_limit\" >= 0"));

        // Giai đoạn 14.7 Track B §B1: the persona vocabulary is a closed set, and only an agent row may
        // carry one. Two constraints rather than one because they fail for different reasons and an operator
        // should be able to tell "typo in the persona key" from "somebody gave a human a persona".
        builder.ToTable("workspace_members", table => table
            .HasCheckConstraint(
                "ck_workspace_members_persona_key",
                "\"persona_key\" IS NULL OR \"persona_key\" IN "
                + "('general', 'scrum_master', 'tech_lead', 'qa_security')"));

        builder.ToTable("workspace_members", table => table
            .HasCheckConstraint(
                "ck_workspace_members_persona_human",
                "\"member_type\" <> 'human' OR \"persona_key\" IS NULL"));

        // One user has exactly one role per workspace.
        builder.HasKey(wm => new { wm.WorkspaceId, wm.UserId });

        builder.Property(wm => wm.Role)
            .HasConversion<string>()      // enum stored as text + CHECK (DB design §4)
            .HasMaxLength(32);

        // Stored lowercase ('human'/'ai_agent') unlike the other enums — see the converter for why.
        // The DB default lets rows that predate Phase 7 read back as 'human' without a data patch.
        builder.Property(wm => wm.MemberType)
            .HasConversion(new MemberTypeToStringConverter())
            .HasMaxLength(16)
            .HasDefaultValue(MemberType.Human);

        builder.Property(wm => wm.AiAgentName)
            .HasMaxLength(120);

        // ---- Giai đoạn 14.7 §A2: capacity profile -----------------------------------------------
        // A jsonb array of canonical slugs. The provider maps List<string> straight onto jsonb, so the
        // Ai/Board services read a real list instead of parsing a delimited string.
        builder.Property(wm => wm.Skills)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        // Free text (not an enum) so adding a role is a service-level vocabulary change, not a migration.
        builder.Property(wm => wm.PrimaryRole)
            .HasMaxLength(50)
            .HasDefaultValue("Member")
            .IsRequired();

        // Defaults matter here: every existing row becomes a full-time member with a WIP limit of 5
        // without a backfill, which is exactly the pre-14.7 behaviour.
        builder.Property(wm => wm.CapacityWeight)
            .HasDefaultValue(1.0)
            .IsRequired();

        builder.Property(wm => wm.MaxWipLimit)
            .HasDefaultValue(5)
            .IsRequired();

        // ---- Giai đoạn 14.7 Track B §B1: persona ------------------------------------------------
        // Free text (not an enum) for the same reason PrimaryRole is: the persona vocabulary lives in
        // PersonaCatalog on the Ai side, and Board must not reference Ai to learn it.
        builder.Property(wm => wm.PersonaKey)
            .HasMaxLength(40);

        // One AI Agent row per persona. The index is built on COALESCE(persona_key, 'general') because
        // PostgreSQL considers every NULL distinct: without the COALESCE, two rows with persona_key NULL
        // would both be accepted and the "one row per persona" rule would be unenforceable.
        //
        // This REPLACES uq_workspace_members_ai_agent ("exactly one agent per workspace", Phase 7 D1).
        // The migration backfills persona_key = 'general' for every existing agent row FIRST, so the
        // Phase 7 lookup (WHERE member_type = 'ai_agent') keeps resolving to the same row.
        //
        // The expression itself is written as raw SQL in the migration (`Npgsql:IndexExpression` is not a
        // real EF Core annotation, and EF cannot express an expression index). The model therefore declares
        // the columns and the predicate only — enough for EF to track the index by name and for the shape
        // to be reviewable — and the generated migration is edited to use the COALESCE expression.
        builder.HasIndex(wm => new { wm.WorkspaceId, wm.PersonaKey })
            .IsUnique()
            .HasFilter("\"member_type\" = 'ai_agent'")
            .HasDatabaseName("uq_workspace_members_ai_persona");

        builder.HasOne(wm => wm.Workspace)
            .WithMany()
            .HasForeignKey(wm => wm.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(wm => wm.User)
            .WithMany()
            .HasForeignKey(wm => wm.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Keep consistent with Workspace's soft-delete query filter (EF Core model
        // validation): a membership is only visible while its workspace is not deleted.
        builder.HasQueryFilter(wm => wm.Workspace == null || wm.Workspace.DeletedAt == null);
    }
}
