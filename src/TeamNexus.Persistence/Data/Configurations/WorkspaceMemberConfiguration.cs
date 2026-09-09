using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

public sealed class WorkspaceMemberConfiguration : IEntityTypeConfiguration<WorkspaceMember>
{
    public void Configure(EntityTypeBuilder<WorkspaceMember> builder)
    {
        builder.ToTable("workspace_members", table => table
            .HasCheckConstraint("ck_workspace_members_role", "\"role\" IN ('Admin', 'Manager', 'Member')"));

        // One user has exactly one role per workspace.
        builder.HasKey(wm => new { wm.WorkspaceId, wm.UserId });

        builder.Property(wm => wm.Role)
            .HasConversion<string>()      // enum stored as text + CHECK (DB design §4)
            .HasMaxLength(32);

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
