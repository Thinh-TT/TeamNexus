using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// Result of processing an inbound GitHub webhook event (Phase 15.3 §T15.3.3).
/// </summary>
public sealed record GitHubWebhookProcessResult(
    string Status,
    bool IsDuplicate = false,
    string? Summary = null,
    string? ErrorMessage = null);

/// <summary>
/// Core inbound webhook handler and PR state machine processor (Phase 15.3 §T15.3.3).
/// </summary>
public interface IGitHubWebhookService
{
    /// <summary>
    /// Processes an authenticated inbound GitHub webhook event delivery.
    /// </summary>
    Task<GitHubWebhookProcessResult> ProcessWebhookAsync(
        WorkspaceGitHubIntegration integration,
        string eventName,
        string deliveryId,
        ReadOnlyMemory<byte> rawBody,
        CancellationToken ct = default);
}
