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
    /// <param name="assigneeIsAiAgent">
    /// Phase 7 §3.3 — resolved ONCE per page by the caller (there is exactly one agent per
    /// workspace), not per task. Optional/trailing so the original call sites keep compiling.
    /// </param>
    /// <param name="activeAgentRunId">
    /// Phase 7 §3.3 — newest live agent run of this task, from <see cref="AgentRunLookup"/>.
    /// </param>
    public static TaskResponse MapTask(
        BoardTask task,
        IReadOnlyList<LabelResponse> labels,
        int commentCount = 0,
        bool assigneeIsAiAgent = false,
        Guid? activeAgentRunId = null)
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
            commentCount,
            assigneeIsAiAgent,
            activeAgentRunId);

    public static LabelResponse MapLabel(Label label)
        => new(label.Id, label.WorkspaceId, label.Name, label.Color, label.CreatedAt);
}
