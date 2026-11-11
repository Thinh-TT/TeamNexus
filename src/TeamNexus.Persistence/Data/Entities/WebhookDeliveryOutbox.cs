namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Transactional outbox row for outbound Slack / Discord webhook deliveries (Phase 15.1 §T15.1.2 / 15.2).
/// Table: <c>webhook_delivery_outbox</c>.
/// </summary>
public class WebhookDeliveryOutbox : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EndpointId { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid? NotificationId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string? DedupeKey { get; set; }

    public string PayloadJson { get; set; } = "{}";

    /// <summary>Delivery lifecycle status: <c>"Pending"</c>, <c>"Sent"</c>, or <c>"Failed"</c>.</summary>
    public string Status { get; set; } = "Pending";

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; set; } = 5;

    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? SentAt { get; set; }

    public int? LastHttpStatus { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public WorkspaceWebhookEndpoint? Endpoint { get; set; }
}

