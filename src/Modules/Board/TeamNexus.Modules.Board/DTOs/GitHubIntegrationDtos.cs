namespace TeamNexus.Modules.Board.DTOs;

/// <summary>
/// Response payload for workspace GitHub integration settings.
/// Invariant: Never returns plaintext Personal Access Token (PAT) or Webhook Secret.
/// </summary>
public sealed record GitHubIntegrationResponse(
    Guid WorkspaceId,
    string Provider,
    string? RepoName,
    bool HasPat,
    string? MaskedPat,
    bool HasWebhookSecret,
    bool IsEnabled,
    string WebhookUrl,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Request payload to update or configure workspace GitHub integration.
/// If PersonalAccessToken or WebhookSecret is null or empty, existing secrets are retained.
/// </summary>
public sealed record UpdateGitHubIntegrationRequest(
    string? RepoName,
    string? PersonalAccessToken,
    string? WebhookSecret,
    bool? IsEnabled);

/// <summary>
/// Response payload for testing GitHub repository connection via REST API.
/// </summary>
public sealed record TestGitHubConnectionResponse(
    bool Success,
    string? RepoFullName,
    string? DefaultBranch,
    string? ErrorMessage);
