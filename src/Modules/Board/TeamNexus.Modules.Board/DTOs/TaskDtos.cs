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
/// </summary>
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
    int CommentCount);
