namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Webhook delivery log for tracking processed webhooks and enforcing idempotency.
/// Table: webhook_delivery_logs (Phase 15 §1.3).
/// </summary>
public class WebhookDeliveryLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string DeliveryId { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }
}
