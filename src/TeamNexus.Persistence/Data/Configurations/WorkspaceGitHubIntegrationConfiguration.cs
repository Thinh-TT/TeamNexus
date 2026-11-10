using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data.Configurations;

public sealed class WorkspaceGitHubIntegrationConfiguration : IEntityTypeConfiguration<WorkspaceGitHubIntegration>
{
    public void Configure(EntityTypeBuilder<WorkspaceGitHubIntegration> builder)
    {
        builder.ToTable("workspace_github_integrations");

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.WorkspaceId)
            .IsUnique()
            .HasDatabaseName("uq_workspace_github_integrations_workspace_id");

        builder.HasIndex(x => x.WebhookToken)
            .IsUnique()
            .HasDatabaseName("uq_workspace_github_integrations_webhook_token");

        builder.Property(x => x.SecretCiphertext)
            .IsRequired();

        builder.Property(x => x.SecretHint)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(x => x.IsEnabled)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.IssueLabelFilter)
            .HasMaxLength(500);

        builder.Property(x => x.AutoMoveOnPr)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.AutoCreateTasksFromIssues)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.SyncIssueComments)
            .HasDefaultValue(true)
            .IsRequired();

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(x => x.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Board>()
            .WithMany()
            .HasForeignKey(x => x.DefaultBoardId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

