using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

/// <summary>
/// EF mapping for workspace invitations (Phase 11 / DB design §3.3): snake_case table, role and
/// status as text + CHECK, SHA-256 token hash with a unique index, RESTRICT FKs.
/// <para>
/// <b>No query filter on purpose.</b> The invitation belongs to the workspace, but unlike
/// <c>workspace_members</c> it must stay reachable while the workspace is soft-deleted: the
/// partial unique index on <c>(workspace_id, invited_email) WHERE status = 'Pending'</c> is enforced
/// by PostgreSQL regardless of the app filter, so hiding rows here would only produce confusing
/// duplicate-key failures instead of a clean domain error.
/// </para>
/// </summary>
public sealed class WorkspaceInvitationConfiguration : IEntityTypeConfiguration<WorkspaceInvitation>
{
    public void Configure(EntityTypeBuilder<WorkspaceInvitation> builder)
    {
        builder.ToTable("workspace_invitations", table =>
        {
            table.HasCheckConstraint(
                "ck_workspace_invitations_invited_role",
                "\"invited_role\" IN ('Admin', 'Manager', 'Member')");

            table.HasCheckConstraint(
                "ck_workspace_invitations_status",
                "\"status\" IN ('Pending', 'Accepted', 'Cancelled', 'Expired')");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.InvitedEmail)
            .HasMaxLength(320)
            .IsRequired();

        // Enum stored as text + CHECK — same rule as workspace_members.role (DB design §4).
        builder.Property(x => x.InvitedRole)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.TokenHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        // One email can never have two LIVE invitations for the same workspace (DB design §3.3).
        // A cancelled/expired invitation is outside the filter, so re-inviting is allowed.
        builder.HasIndex(x => new { x.WorkspaceId, x.InvitedEmail })
            .IsUnique()
            .HasFilter("\"status\" = 'Pending'")
            .HasDatabaseName("uq_workspace_invitations_pending");

        // The raw token is only ever looked up by hash, and the hash is globally unique (mirrors
        // refresh_tokens.token_hash — Phase 1).
        builder.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasDatabaseName("uq_workspace_invitations_token_hash");

        // Pending-invitation listing and cancellation for one workspace.
        builder.HasIndex(x => new { x.WorkspaceId, x.Status });

        // No physical cascade anywhere (DB design §7): every FK is Restrict.
        // Declared optional (the navigation is never traversed — the API resolves the workspace by
        // id) so EF does not demand a matching query filter for the soft-deletable Workspace
        // principal: with a required navigation EF warns that a filtered principal may "filter out"
        // the dependent, which is exactly the behaviour we want here (invitations must stay
        // readable while the workspace is soft-deleted).
        builder.HasOne(x => x.Workspace)
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.InvitedBy)
            .WithMany()
            .HasForeignKey(x => x.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // AcceptedByUserId is an audit pointer, not a relationship the API navigates: declared
        // without a navigation so EF cannot inherit a filter from the principal (Phase 5 pattern).
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.AcceptedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
