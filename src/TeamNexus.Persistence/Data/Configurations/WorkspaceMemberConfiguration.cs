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
        });

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
