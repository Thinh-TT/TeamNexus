namespace TeamNexus.Modules.Ai.DTOs.Integrations;

/// <summary>
/// Workspace GitHub integration configuration response (Phase 15.3 §T15.3.2).
/// Note: <see cref="WebhookSecretPlaintext"/> is strictly populated ONLY ONCE upon creation or rotation.
/// </summary>
public sealed record WorkspaceGitHubIntegrationResponse(
    Guid Id,
    Guid WorkspaceId,
    Guid WebhookToken,
    string WebhookPayloadUrl,
    string SecretHint,
    bool IsEnabled,
    Guid? DefaultBoardId,
    string? IssueLabelFilter,
    bool AutoMoveOnPr,
    bool AutoCreateTasksFromIssues,
    bool SyncIssueComments,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? WebhookSecretPlaintext = null);

/// <summary>
/// Request payload to configure or update workspace GitHub integration (Phase 15.3 §T15.3.2).
/// </summary>
public sealed record UpsertWorkspaceGitHubIntegrationRequest(
    bool? IsEnabled = null,
    Guid? DefaultBoardId = null,
    string? IssueLabelFilter = null,
    bool? AutoMoveOnPr = null,
    bool? AutoCreateTasksFromIssues = null,
    bool? SyncIssueComments = null);

/// <summary>
/// Response payload after rotating the GitHub webhook secret (Phase 15.3 §T15.3.2).
/// Returns the plaintext secret exactly once.
/// </summary>
public sealed record RotateGitHubSecretResponse(
    string SecretHint,
    string WebhookSecretPlaintext);

/// <summary>
/// Inbound GitHub webhook delivery ledger entry for administrative audit (Phase 15.3 §T15.3.2).
/// </summary>
public sealed record GitHubWebhookDeliveryResponse(
    Guid Id,
    string DeliveryId,
    string EventName,
    string? Action,
    string? RepositoryFullName,
    string Status,
    string? Summary,
    string? ErrorMessage,
    DateTimeOffset ReceivedAt);
