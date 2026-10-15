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

        // Exactly ONE AI Agent per workspace (Phase 7 §2.1 / DB design §5). Always locate the agent
        // through member_type — AiAgentName is display data and may be renamed.
        builder.HasIndex(wm => wm.WorkspaceId)
            .IsUnique()
            .HasFilter("\"member_type\" = 'ai_agent'")
            .HasDatabaseName("uq_workspace_members_ai_agent");

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
