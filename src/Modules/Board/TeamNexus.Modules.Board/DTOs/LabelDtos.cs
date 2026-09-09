namespace TeamNexus.Modules.Board.DTOs;

/// <summary>Create a workspace-scoped label.</summary>
public sealed record CreateLabelRequest(string Name, string Color);

/// <summary>Body for attaching an existing label to a task (POST /api/tasks/{taskId}/labels).</summary>
public sealed record AttachLabelRequest(Guid LabelId);

public sealed record LabelResponse(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string Color,
    DateTimeOffset CreatedAt);
