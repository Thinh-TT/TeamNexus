using TeamNexus.Modules.Board.DTOs;

namespace TeamNexus.Modules.Board.Services.Integrations;

/// <summary>
/// Service managing workspace GitHub integration credentials, repository binding and connectivity checks.
/// Restrict all operations to workspace Managers and Admins (Phase 15 §1.1).
/// </summary>
public interface IGitHubIntegrationService
{
    Task<GitHubIntegrationResponse?> GetAsync(Guid workspaceId, Guid callerUserId, CancellationToken ct = default);
    Task<GitHubIntegrationResponse> UpsertAsync(Guid workspaceId, Guid callerUserId, UpdateGitHubIntegrationRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid workspaceId, Guid callerUserId, CancellationToken ct = default);
    Task<TestGitHubConnectionResponse> TestConnectionAsync(Guid workspaceId, Guid callerUserId, CancellationToken ct = default);
}
