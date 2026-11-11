using TeamNexus.Modules.Ai.DTOs.Integrations;

namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// Workspace-level administration for GitHub webhook integration (Phase 15.3 §T15.3.2).
/// </summary>
public interface IWorkspaceGitHubIntegrationService
{
    /// <summary>
    /// Gets the current GitHub integration configuration for the given workspace, or <c>null</c> if not yet configured.
    /// </summary>
    Task<WorkspaceGitHubIntegrationResponse?> GetAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Initializes or updates the GitHub integration settings for the given workspace.
    /// On initial creation, <see cref="WorkspaceGitHubIntegrationResponse.WebhookSecretPlaintext"/> is populated exactly once.
    /// </summary>
    Task<WorkspaceGitHubIntegrationResponse> UpsertAsync(
        Guid workspaceId,
        UpsertWorkspaceGitHubIntegrationRequest request,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a new 32-byte webhook secret, invalidating the previous secret, and returns the plaintext secret exactly once.
    /// </summary>
    Task<RotateGitHubSecretResponse> RotateSecretAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Deletes or disables the workspace GitHub integration configuration.
    /// </summary>
    Task DeleteAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Returns the recent webhook delivery history for troubleshooting and verification.
    /// </summary>
    Task<IReadOnlyList<GitHubWebhookDeliveryResponse>> GetDeliveriesAsync(
        Guid workspaceId,
        Guid userId,
        int take = 30,
        CancellationToken ct = default);
}
