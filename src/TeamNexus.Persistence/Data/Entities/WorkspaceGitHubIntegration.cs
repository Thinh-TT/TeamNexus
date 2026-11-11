namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Per-workspace inbound GitHub webhook integration settings (Phase 15.1 §T15.1.2 / 15.3).
/// Table: <c>workspace_github_integrations</c> (1–1 with <c>workspaces</c>).
/// </summary>
public class WorkspaceGitHubIntegration : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    /// <summary>Opaque public routing token used in <c>/api/webhooks/github/{webhookToken}</c>.</summary>
    public Guid WebhookToken { get; set; } = Guid.NewGuid();

    /// <summary>DataProtection-encrypted HMAC-SHA256 secret (never returned in GET responses).</summary>
    public string SecretCiphertext { get; set; } = string.Empty;

    /// <summary>Last 4 characters of the plaintext secret for administrative display.</summary>
    public string SecretHint { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public Guid? DefaultBoardId { get; set; }

    public string? IssueLabelFilter { get; set; }

    public bool AutoMoveOnPr { get; set; } = true;

    public bool AutoCreateTasksFromIssues { get; set; } = true;

    public bool SyncIssueComments { get; set; } = true;

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<GitHubWebhookDelivery> Deliveries { get; set; } = new List<GitHubWebhookDelivery>();
}

