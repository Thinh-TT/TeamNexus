using TeamNexus.Modules.Ai.DTOs.Integrations;

namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// Management service for workspace outbound Slack/Discord webhook endpoints, test pings,
/// and delivery outbox inspection (Phase 15.2 §T15.2.4).
/// </summary>
public interface IWorkspaceWebhookService
{
    Task<WorkspaceWebhookListResponse> ListAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken ct = default);

    Task<WorkspaceWebhookEndpointResponse> CreateAsync(
        Guid workspaceId,
        CreateWorkspaceWebhookRequest request,
        Guid userId,
        CancellationToken ct = default);

    Task<WorkspaceWebhookEndpointResponse> UpdateAsync(
        Guid workspaceId,
        Guid endpointId,
        UpdateWorkspaceWebhookRequest request,
        Guid userId,
        CancellationToken ct = default);

    Task DeleteAsync(
        Guid workspaceId,
        Guid endpointId,
        Guid userId,
        CancellationToken ct = default);

    Task<WebhookTestResultResponse> SendTestAsync(
        Guid workspaceId,
        Guid endpointId,
        Guid userId,
        CancellationToken ct = default);

    Task<IReadOnlyList<WebhookDeliveryItemResponse>> GetDeliveriesAsync(
        Guid workspaceId,
        Guid endpointId,
        Guid userId,
        CancellationToken ct = default);
}

