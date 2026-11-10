using Microsoft.AspNetCore.Http;

namespace TeamNexus.Modules.Board.Services.Integrations;

/// <summary>
/// Handles incoming GitHub webhook events (Phase 15 §1.3), verifying authenticity,
/// ensuring idempotent delivery, and triggering automated board actions such as
/// transitioning tasks to Done when a linked pull request is merged.
/// </summary>
public interface IGitHubWebhookHandler
{
    /// <summary>
    /// Processes a GitHub webhook payload.
    /// </summary>
    /// <param name="eventType">The value of the X-GitHub-Event header (e.g. "ping", "pull_request").</param>
    /// <param name="signature">The value of the X-Hub-Signature-256 header.</param>
    /// <param name="deliveryId">The value of the X-GitHub-Delivery header (unique GUID per delivery).</param>
    /// <param name="rawBody">The raw unparsed HTTP request body bytes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP result response for GitHub.</returns>
    Task<IResult> HandleWebhookAsync(
        string? eventType,
        string? signature,
        string? deliveryId,
        byte[] rawBody,
        CancellationToken ct = default);
}
