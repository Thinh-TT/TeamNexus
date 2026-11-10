using TeamNexus.Persistence.Data;

namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Workspace integration configuration with external providers (e.g. GitHub).
/// Table: workspace_integrations (Phase 15 §1.1).
/// </summary>
public class WorkspaceIntegration : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string? RepoName { get; set; }

    public string? EncryptedPat { get; set; }

    public string? EncryptedWebhookSecret { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Workspace? Workspace { get; set; }
}
