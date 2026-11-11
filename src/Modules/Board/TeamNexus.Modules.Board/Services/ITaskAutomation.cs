using TeamNexus.Modules.Board.DTOs;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Target system column for automated task movement (Phase 15.1 §T15.1.5).
/// </summary>
public enum SystemColumnTarget
{
    InReview,
    Done,
}

/// <summary>
/// Snapshot returned by <see cref="ITaskAutomation.MoveToSystemColumnAsync"/> so the caller
/// (e.g., <c>GitHubWebhookService</c>) can record a reversible <c>AiActionLog</c> entry
/// (<c>BeforeSnapshot</c> and <c>AppliedSnapshot</c>).
/// </summary>
public sealed record TaskAutomationMoveResult(
    bool Moved,
    Guid TaskId,
    Guid BoardId,
    Guid WorkspaceId,
    Guid FromColumnId,
    Guid ToColumnId,
    int PreviousPosition,
    int NewPosition,
    DateTimeOffset? PreviousCompletedAt,
    DateTimeOffset? NewCompletedAt,
    TaskResponse? Task = null,
    string? SkippedReason = null);

/// <summary>
/// Narrow internal port exposed by the <c>Board</c> module so automated system workflows in <c>Ai</c>
/// (such as GitHub PR / Issue / Comment webhooks) can mutate tasks and comments under the
/// <c>GitHub Bot</c> system identity without requiring interactive <c>workspace_members</c> checks,
/// while preserving SignalR broadcasts, <c>CompletedAt</c> rules, and <c>ActivityLog</c> recording
/// (Phase 15.1 §T15.1.5).
/// </summary>
public interface ITaskAutomation
{
    Task<TaskAutomationMoveResult> MoveToSystemColumnAsync(
        Guid taskId,
        SystemColumnTarget target,
        string reason,
        CancellationToken ct = default);

    Task<TaskResponse> CreateTaskFromExternalIssueAsync(
        Guid boardId,
        string title,
        string? description,
        CancellationToken ct = default);

    Task<TaskResponse> ApplySystemTriageSuggestionsAsync(
        Guid taskId,
        string? priority,
        decimal? estimatedHours,
        IReadOnlyList<string>? tags,
        Guid? assigneeId,
        CancellationToken ct = default);

    Task<CommentResponse> AddSystemCommentAsync(
        Guid taskId,
        Guid systemUserId,
        string markdownContent,
        CancellationToken ct = default);
}

