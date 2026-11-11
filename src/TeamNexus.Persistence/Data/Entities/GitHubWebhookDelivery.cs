namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Idempotency ledger and audit trail for inbound <c>X-GitHub-Delivery</c> events (Phase 15.1 §T15.1.2 / 15.3).
/// Table: <c>github_webhook_deliveries</c>.
/// </summary>
public class GitHubWebhookDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid IntegrationId { get; set; }

    public Guid WorkspaceId { get; set; }

    public string DeliveryId { get; set; } = string.Empty;

    public string EventName { get; set; } = string.Empty;

    public string? Action { get; set; }

    public string? RepositoryFullName { get; set; }

    /// <summary>Processing outcome: <c>"Processed"</c>, <c>"Ignored"</c>, or <c>"Failed"</c>.</summary>
    public string Status { get; set; } = "Processed";

    public string? Summary { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;

    public WorkspaceGitHubIntegration? Integration { get; set; }
}

