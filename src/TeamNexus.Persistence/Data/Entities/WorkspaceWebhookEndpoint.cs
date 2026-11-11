namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Outbound Slack / Discord webhook endpoint configured for a workspace (Phase 15.1 §T15.1.2 / 15.2).
/// Table: <c>workspace_webhook_endpoints</c>.
/// </summary>
public class WorkspaceWebhookEndpoint : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Target chat provider: <c>"slack"</c> or <c>"discord"</c>.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Validated HTTPS incoming webhook URL.</summary>
    public string TargetUrl { get; set; } = string.Empty;

    /// <summary>Comma-separated list of subscribed notification event types.</summary>
    public string SubscribedEvents { get; set; } = "ObserverAlert,AgentActionPending,ClarificationNeeded";

    public bool IsActive { get; set; } = true;

    public int FailureCount { get; set; }

    public DateTimeOffset? LastSuccessAt { get; set; }

    public DateTimeOffset? LastFailureAt { get; set; }

    public string? LastError { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<WebhookDeliveryOutbox> Deliveries { get; set; } = new List<WebhookDeliveryOutbox>();
}

