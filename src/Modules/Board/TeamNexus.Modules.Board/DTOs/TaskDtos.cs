namespace TeamNexus.Modules.Board.DTOs;

/// <summary>Priority is transferred as text ("Low"/"Medium"/"High"/"Urgent"), matching storage.</summary>
public sealed record CreateTaskRequest(
    Guid ColumnId,
    string Title,
    string? Description,
    Guid? AssigneeId,
    DateTimeOffset? DueDate,
    string? Priority);

/// <summary>Full metadata update (PUT). null = clear the optional field.</summary>
public sealed record UpdateTaskRequest(
    string Title,
    string? Description,
    Guid? AssigneeId,
    DateTimeOffset? DueDate,
    string? Priority);

/// <summary>Move a task to another column/position (drag & drop).</summary>
public sealed record MoveTaskRequest(Guid ColumnId, int Position);

/// <summary>
/// Task payload for the UI: assignee display name, attached labels and comment count
/// are resolved by the service.
/// <para>
/// Phase 7 §3.1 added the two trailing fields. They are appended (never reordered) so older
/// clients keep working, and the resolved values are supplied by the service per page — see
/// <c>DtoMapping.MapTask</c>.
/// </para>
/// </summary>
/// <param name="AssigneeIsAiAgent">
/// True when <see cref="AssigneeId"/> is the AI Agent member (<c>member_type = 'ai_agent'</c>) of
/// the task's workspace. Resolved once per page via <c>IAiAgentResolver</c>.
/// </param>
/// <param name="ActiveAgentRunId">
/// The task's newest "live" agent run (Running, or waiting for clarification/approval), else null.
/// <b>Not</b> the single source of truth: the UI must still GET the run on mount/reconnect because a
/// SignalR event can be missed while the app sleeps (Phase 7 §5C).
/// </param>
public sealed record TaskResponse(
    Guid Id,
    Guid BoardId,
    Guid ColumnId,
    string Title,
    string? Description,
    int Position,
    Guid? AssigneeId,
    string? AssigneeName,
    DateTimeOffset? DueDate,
    string? Priority,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<LabelResponse> Labels,
    int CommentCount,
    bool AssigneeIsAiAgent,
    Guid? ActiveAgentRunId);
