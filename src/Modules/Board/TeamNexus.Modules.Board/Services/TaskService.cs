using Microsoft.EntityFrameworkCore;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;
using BoardEntity = TeamNexus.Persistence.Data.Entities.Board;

namespace TeamNexus.Modules.Board.Services;

public interface ITaskService
{
    Task<IReadOnlyList<TaskResponse>> GetTasksAsync(Guid boardId, Guid? columnId, Guid userId, CancellationToken ct = default);

    Task<TaskResponse> GetTaskByIdAsync(Guid taskId, Guid userId, CancellationToken ct = default);

    Task<TaskResponse> CreateTaskAsync(Guid boardId, CreateTaskRequest request, Guid userId, CancellationToken ct = default);

    Task<TaskResponse> UpdateTaskAsync(Guid taskId, UpdateTaskRequest request, Guid userId, CancellationToken ct = default);

    Task<TaskResponse> MoveTaskAsync(Guid taskId, MoveTaskRequest request, Guid userId, CancellationToken ct = default);

    Task DeleteTaskAsync(Guid taskId, Guid userId, CancellationToken ct = default);
}

public sealed class TaskService : ITaskService
{
    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IBoardEventPublisher _events;

    public TaskService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IBoardEventPublisher events)
    {
        _db = db;
        _access = access;
        _events = events;
    }

    public async Task<IReadOnlyList<TaskResponse>> GetTasksAsync(
        Guid boardId, Guid? columnId, Guid userId, CancellationToken ct = default)
    {
        var board = await RequireVisibleBoardAsync(boardId, userId, ct);

        var query = _db.Tasks.AsNoTracking()
            .Where(t => t.BoardId == boardId);

        if (columnId.HasValue)
        {
            query = query.Where(t => t.ColumnId == columnId.Value);
        }

        var tasks = await query
            .Include(t => t.Assignee)
            .OrderBy(t => t.Position)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);

        var taskIds = tasks.Select(t => t.Id).ToList();
        var counts = await LoadCommentCountsAsync(taskIds, ct);
        var labelsByTask = await LoadLabelsByTaskAsync(taskIds, ct);

        return tasks.Select(t => DtoMapping.MapTask(
            t, labelsByTask.GetValueOrDefault(t.Id, []), counts.GetValueOrDefault(t.Id))).ToList();
    }

    public async Task<TaskResponse> GetTaskByIdAsync(Guid taskId, Guid userId, CancellationToken ct = default)
    {
        var task = await LoadTaskAsync(taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        await RequireMemberOfTaskBoardAsync(task, userId, ct);

        var count = await _db.TaskComments
            .CountAsync(c => c.TaskId == taskId, ct);

        var labels = await LoadLabelsByTaskAsync([taskId], ct);
        return DtoMapping.MapTask(task, labels.GetValueOrDefault(taskId, []), count);
    }

    public async Task<TaskResponse> CreateTaskAsync(
        Guid boardId, CreateTaskRequest request, Guid userId, CancellationToken ct = default)
    {
        var board = await RequireVisibleBoardAsync(boardId, userId, ct);
        ValidateTitle(request.Title);

        var column = await _db.BoardColumns
            .FirstOrDefaultAsync(c => c.Id == request.ColumnId, ct)
            ?? throw new NotFoundException("Column not found.");

        if (column.BoardId != boardId)
        {
            throw new BadRequestException("Column does not belong to this board.");
        }

        var maxPosition = await _db.Tasks
            .Where(t => t.ColumnId == column.Id)
            .MaxAsync(t => (int?)t.Position, ct) ?? -1;

        ApplicationUser? assignee = null;
        if (request.AssigneeId.HasValue)
        {
            assignee = await _db.Users.FirstOrDefaultAsync(u => u.Id == request.AssigneeId.Value, ct)
                ?? throw new BadRequestException("Assignee does not exist.");
        }

        var task = new BoardTask
        {
            BoardId = boardId,
            ColumnId = column.Id,
            Title = request.Title.Trim(),
            Description = TrimToNull(request.Description),
            Position = maxPosition + 1,
            AssigneeId = request.AssigneeId,
            DueDate = request.DueDate,
            Priority = ParsePriority(request.Priority),
            CreatedBy = userId,
            CompletedAt = column.IsDone ? DateTimeOffset.UtcNow : null,
            Assignee = assignee,
        };

        _db.Tasks.Add(task);
        await _db.SaveChangesAsync(ct);

        var response = DtoMapping.MapTask(task, []);
        await _events.TaskCreated(boardId, response, ct);
        return response;
    }

    public async Task<TaskResponse> UpdateTaskAsync(
        Guid taskId, UpdateTaskRequest request, Guid userId, CancellationToken ct = default)
    {
        var task = await LoadTaskAsync(taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        await RequireMemberOfTaskBoardAsync(task, userId, ct);
        ValidateTitle(request.Title);

        if (request.AssigneeId.HasValue
            && !await _db.Users.AnyAsync(u => u.Id == request.AssigneeId.Value, ct))
        {
            throw new BadRequestException("Assignee does not exist.");
        }

        task.Title = request.Title.Trim();
        task.Description = TrimToNull(request.Description);
        task.AssigneeId = request.AssigneeId;
        task.DueDate = request.DueDate;
        task.Priority = ParsePriority(request.Priority);

        await _db.SaveChangesAsync(ct);
        var response = await GetTaskByIdAsync(taskId, userId, ct);
        await _events.TaskUpdated(task.BoardId, response, ct);
        return response;
    }

    public async Task<TaskResponse> MoveTaskAsync(
        Guid taskId, MoveTaskRequest request, Guid userId, CancellationToken ct = default)
    {
        var task = await _db.Tasks
            .Include(t => t.Assignee)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        await RequireMemberOfTaskBoardAsync(task, userId, ct);

        var targetColumn = await _db.BoardColumns
            .FirstOrDefaultAsync(c => c.Id == request.ColumnId, ct)
            ?? throw new NotFoundException("Target column not found.");

        if (targetColumn.BoardId != task.BoardId)
        {
            throw new BadRequestException("Target column does not belong to the same board.");
        }

        var fromColumnId = task.ColumnId;
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        // Rebuild the target column's order: source tasks first (compact), then the moved
        // task inserted at the requested index, then renumber 0..n-1.
        var tasksInColumn = await _db.Tasks
            .Where(t => t.ColumnId == targetColumn.Id)
            .OrderBy(t => t.Position)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);

        tasksInColumn.Remove(task);
        var insertIndex = Math.Clamp(request.Position, 0, tasksInColumn.Count);
        tasksInColumn.Insert(insertIndex, task);

        for (var i = 0; i < tasksInColumn.Count; i++)
        {
            tasksInColumn[i].Position = i;
        }

        task.ColumnId = targetColumn.Id;

        // completed_at follows the "done" lane: set on entering an is_done column,
        // cleared when leaving it.
        task.CompletedAt = targetColumn.IsDone ? DateTimeOffset.UtcNow : null;

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        await _events.TaskMoved(
            task.BoardId,
            new TaskMovedEventPayload(task.Id, fromColumnId, targetColumn.Id, insertIndex),
            ct);

        // Reload with details (assignee/labels) for the response.
        return await GetTaskByIdAsync(taskId, userId, ct);
    }

    public async Task DeleteTaskAsync(Guid taskId, Guid userId, CancellationToken ct = default)
    {
        var task = await _db.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        await RequireMemberOfTaskBoardAsync(task, userId, ct);

        var boardId = task.BoardId;
        task.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _events.TaskDeleted(boardId, taskId, ct);
    }

    // ---- helpers ----------------------------------------------------------

    private async Task<BoardEntity> RequireVisibleBoardAsync(Guid boardId, Guid userId, CancellationToken ct)
    {
        var board = await _db.Boards
            .FirstOrDefaultAsync(b => b.Id == boardId, ct)
            ?? throw new NotFoundException("Board not found.");

        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);
        return board;
    }

    private async Task RequireMemberOfTaskBoardAsync(BoardTask task, Guid userId, CancellationToken ct)
    {
        var board = await _db.Boards
            .FirstOrDefaultAsync(b => b.Id == task.BoardId, ct)
            ?? throw new NotFoundException("Task not found.");

        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);
    }

    private async Task<BoardTask?> LoadTaskAsync(Guid taskId, CancellationToken ct)
        => await _db.Tasks
            .Where(t => t.Id == taskId)
            .Include(t => t.Assignee)
            .FirstOrDefaultAsync(ct);

    private async Task<Dictionary<Guid, IReadOnlyList<LabelResponse>>> LoadLabelsByTaskAsync(
        List<Guid> taskIds, CancellationToken ct)
    {
        if (taskIds.Count == 0)
        {
            return [];
        }

        var links = await _db.TaskLabels
            .Where(tl => taskIds.Contains(tl.TaskId))
            .Include(tl => tl.Label)
            .AsNoTracking()
            .ToListAsync(ct);

        return links
            .Where(tl => tl.Label is not null)
            .GroupBy(tl => tl.TaskId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<LabelResponse>)g.Select(tl => DtoMapping.MapLabel(tl.Label!)).ToList());
    }

    private async Task<Dictionary<Guid, int>> LoadCommentCountsAsync(List<Guid> taskIds, CancellationToken ct)
    {
        if (taskIds.Count == 0)
        {
            return [];
        }

        return await _db.TaskComments
            .Where(c => taskIds.Contains(c.TaskId))
            .GroupBy(c => c.TaskId)
            .Select(g => new { TaskId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TaskId, x => x.Count, ct);
    }

    private static TaskPriority? ParsePriority(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return Enum.TryParse<TaskPriority>(value, ignoreCase: true, out var priority)
            ? priority
            : throw new BadRequestException(
                $"Priority must be one of: Low, Medium, High, Urgent.");
    }

    private static void ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
        {
            throw new BadRequestException("Task title must be 1–200 characters.");
        }
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
