using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Board.Services;

/// <summary>Central DTO mapping used by BoardService and TaskService.</summary>
internal static class DtoMapping
{
    /// <summary>
    /// Maps a task loaded with the <c>Assignee</c> navigation. Labels and comment count
    /// are provided from grouped queries per task set (never N+1).
    /// </summary>
    public static TaskResponse MapTask(BoardTask task, IReadOnlyList<LabelResponse> labels, int commentCount = 0)
        => new(
            task.Id,
            task.BoardId,
            task.ColumnId,
            task.Title,
            task.Description,
            task.Position,
            task.AssigneeId,
            task.Assignee?.DisplayName,
            task.DueDate,
            task.Priority?.ToString(),
            task.CreatedAt,
            task.UpdatedAt,
            task.CompletedAt,
            labels,
            commentCount);

    public static LabelResponse MapLabel(Label label)
        => new(label.Id, label.WorkspaceId, label.Name, label.Color, label.CreatedAt);
}
