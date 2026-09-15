using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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
    private readonly IActivityLogWriter _activityLog;
    private readonly IAiAgentResolver _agents;
    private readonly INotificationWriter _notifications;

    public TaskService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IBoardEventPublisher events,
        IActivityLogWriter activityLog,
        IAiAgentResolver agents,
        INotificationWriter notifications)
    {
        _db = db;
        _access = access;
        _events = events;
        _activityLog = activityLog;
        _agents = agents;
        _notifications = notifications;
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
        var counts = await TaskReadHelpers.LoadCommentCountsAsync(_db, taskIds, ct);
        var labelsByTask = await TaskReadHelpers.LoadLabelsByTaskAsync(_db, taskIds, ct);
        var activeRunIds = await AgentRunLookup.LoadActiveRunIdsAsync(_db, taskIds, ct);
        var assigneeIsAiAgent = await TaskReadHelpers.ResolveAssigneeIsAiAgentAsync(
            _agents, board.WorkspaceId, tasks, ct);

        return tasks.Select(t => DtoMapping.MapTask(
            t,
            labelsByTask.GetValueOrDefault(t.Id, []),
            counts.GetValueOrDefault(t.Id),
            assigneeIsAiAgent,
            // TryGetValue, NOT GetValueOrDefault: the latter returns Guid.Empty for a missing
            // task, which would serialize as "00000000-..." instead of null (real bug, §3 harness).
            activeRunIds.TryGetValue(t.Id, out var runId) ? runId : null)).ToList();
    }

    public async Task<TaskResponse> GetTaskByIdAsync(Guid taskId, Guid userId, CancellationToken ct = default)
    {
        var task = await LoadTaskAsync(taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        var workspaceId = await RequireMemberOfTaskBoardWithWorkspaceAsync(task, userId, ct);

        var count = await _db.TaskComments
            .CountAsync(c => c.TaskId == taskId, ct);

        var labels = await TaskReadHelpers.LoadLabelsByTaskAsync(_db, [taskId], ct);
        var activeRunIds = await AgentRunLookup.LoadActiveRunIdsAsync(_db, [taskId], ct);
        var assigneeIsAiAgent = await TaskReadHelpers.ResolveAssigneeIsAiAgentAsync(
            _agents, workspaceId, [task], ct);

        return DtoMapping.MapTask(
            task,
            labels.GetValueOrDefault(taskId, []),
            count,
            assigneeIsAiAgent,
            // See the note in GetTasksAsync: GetValueOrDefault would produce Guid.Empty, not null.
            activeRunIds.TryGetValue(taskId, out var detailRunId) ? detailRunId : null);
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

        // Phase 7 §3.2: the assignee must be a workspace member, not merely an existing user.
        await RequireAssigneeInWorkspaceAsync(board.WorkspaceId, request.AssigneeId, ct);
        var assignee = await LoadAssigneeAsync(request.AssigneeId, ct);

        var task = new BoardTask
        {
            BoardId = boardId,
            ColumnId = column.Id,
            Title = request.Title.Trim(),
            Description = TrimToNull(request.Description),
            Position = maxPosition + 1,
            AssigneeId = request.AssigneeId,
            DueDate = ToUtc(request.DueDate),
            Priority = ParsePriority(request.Priority),
            CreatedBy = userId,
            CompletedAt = column.IsDone ? DateTimeOffset.UtcNow : null,
            Assignee = assignee,
        };

        _db.Tasks.Add(task);
        await _db.SaveChangesAsync(ct);

        // A brand-new task has no agent run yet, so the defaults (false / null) are correct.
        var response = DtoMapping.MapTask(task, []);
        await _events.TaskCreated(boardId, response, ct);

        // Phase 5 §2.3: recorded after the write so the log reflects what was actually stored
        // (columns: entity_id/board_id/workspace_id — payload only carries the short diff).
        await _activityLog.RecordAsync(new ActivityLogEntry(
            board.WorkspaceId,
            boardId,
            userId,
            ObserverEntityTypes.Task,
            task.Id,
            ObserverActivityActions.TaskCreated,
            ActivityPayload(new
            {
                columnId = column.Id,
                assigneeId = request.AssigneeId,
                priority = task.Priority?.ToString(),
                isDone = column.IsDone,
            })), ct);

        // Phase 11 §6.5: a brand-new assignment is the one case where the assignee definitely did
        // not know about this task yet. Best-effort and AFTER the writes, like the activity log.
        await NotifyAssignmentAsync(board.WorkspaceId, task, request.AssigneeId, userId, ct);

        return response;
    }

    public async Task<TaskResponse> UpdateTaskAsync(
        Guid taskId, UpdateTaskRequest request, Guid userId, CancellationToken ct = default)
    {
        var task = await LoadTaskAsync(taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        var workspaceId = await RequireMemberOfTaskBoardWithWorkspaceAsync(task, userId, ct);
        ValidateTitle(request.Title);

        // Phase 7 §3.2: same membership rule as create — a task may only be assigned to a member
        // of its own workspace (previously this only checked the users table).
        await RequireAssigneeInWorkspaceAsync(workspaceId, request.AssigneeId, ct);

        // Capture the diff basis BEFORE mutating (Phase 5 §2.3 — text fields are reported as
        // booleans so the log never stores the title/description content).
        var titleChanged = !string.Equals(task.Title, request.Title.Trim(), StringComparison.Ordinal);
        var descriptionChanged = !string.Equals(task.Description, TrimToNull(request.Description), StringComparison.Ordinal);

        // Phase 11 §6.5: capture the assignment BEFORE the mutation. An alert is sent only when the
        // assignee actually changes — editing a title must not re-notify the same person.
        var previousAssigneeId = task.AssigneeId;

        task.Title = request.Title.Trim();
        task.Description = TrimToNull(request.Description);
        task.AssigneeId = request.AssigneeId;
        task.DueDate = ToUtc(request.DueDate);
        task.Priority = ParsePriority(request.Priority);

        await _db.SaveChangesAsync(ct);

        await _activityLog.RecordAsync(new ActivityLogEntry(
            workspaceId,
            task.BoardId,
            userId,
            ObserverEntityTypes.Task,
            task.Id,
            ObserverActivityActions.TaskUpdated,
            ActivityPayload(new
            {
                titleChanged,
                descriptionChanged,
                assigneeId = request.AssigneeId,
                dueDate = task.DueDate,
                priority = task.Priority?.ToString(),
            })), ct);

        var response = await GetTaskByIdAsync(taskId, userId, ct);
        await _events.TaskUpdated(task.BoardId, response, ct);

        if (previousAssigneeId != request.AssigneeId)
        {
            await NotifyAssignmentAsync(workspaceId, task, request.AssigneeId, userId, ct);
        }

        return response;
    }

    public async Task<TaskResponse> MoveTaskAsync(
        Guid taskId, MoveTaskRequest request, Guid userId, CancellationToken ct = default)
    {
        var task = await _db.Tasks
            .Include(t => t.Assignee)
            .Include(t => t.Board)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        var workspaceId = await RequireMemberOfTaskBoardWithWorkspaceAsync(task, userId, ct);

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

        // Phase 5 §2.3: MUST be after CommitAsync — an activity row must never be part of (or
        // roll back with) the drag & drop transaction.
        await _activityLog.RecordAsync(new ActivityLogEntry(
            workspaceId,
            task.BoardId,
            userId,
            ObserverEntityTypes.Task,
            task.Id,
            ObserverActivityActions.TaskMoved,
            ActivityPayload(new
            {
                fromColumnId,
                toColumnId = targetColumn.Id,
                position = insertIndex,
            })), ct);

        // Entering an is_done column is a second, distinct event (set by the Board logic above).
        if (targetColumn.IsDone)
        {
            await _activityLog.RecordAsync(new ActivityLogEntry(
                workspaceId,
                task.BoardId,
                userId,
                ObserverEntityTypes.Task,
                task.Id,
                ObserverActivityActions.TaskCompleted,
                ActivityPayload(new { columnId = targetColumn.Id })), ct);
        }

        // Reload with details (assignee/labels) for the response.
        return await GetTaskByIdAsync(taskId, userId, ct);
    }

    public async Task DeleteTaskAsync(Guid taskId, Guid userId, CancellationToken ct = default)
    {
        var task = await _db.Tasks
            .Include(t => t.Board)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        var workspaceId = await RequireMemberOfTaskBoardWithWorkspaceAsync(task, userId, ct);

        var boardId = task.BoardId;
        var columnId = task.ColumnId;

        task.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _events.TaskDeleted(boardId, taskId, ct);

        // Phase 5 §2.3: after the soft delete; workspaceId was captured before it because the
        // task row (with its board navigation) is filtered out from now on.
        await _activityLog.RecordAsync(new ActivityLogEntry(
            workspaceId,
            boardId,
            userId,
            ObserverEntityTypes.Task,
            taskId,
            ObserverActivityActions.TaskDeleted,
            ActivityPayload(new { columnId })), ct);
    }

    // ---- helpers ----------------------------------------------------------

    /// <summary>camelCase JSON for activity payloads (Phase 5 §2, decision A6).</summary>
    private static readonly JsonSerializerOptions ActivityJson = new(JsonSerializerDefaults.Web);

    private static string ActivityPayload(object value) => JsonSerializer.Serialize(value, ActivityJson);

    /// <summary>
    /// Phase 7 §3.2 — the assignee must be a member of the workspace that owns the task, not merely
    /// an existing <c>users</c> row. Before Phase 7 this check did not exist, so a task could be
    /// assigned to a user who was never in the workspace; the AI Agent (a real <c>users</c> row)
    /// would have become a new entry point for that bug.
    /// </summary>
    /// <remarks>A <c>null</c> assignee (unassigned) is always valid and costs no query.</remarks>
    private async Task RequireAssigneeInWorkspaceAsync(
        Guid workspaceId, Guid? assigneeId, CancellationToken ct)
    {
        if (assigneeId is null)
        {
            return;
        }

        var isMember = await _db.WorkspaceMembers
            .AnyAsync(wm => wm.WorkspaceId == workspaceId && wm.UserId == assigneeId.Value, ct);

        if (!isMember)
        {
            throw new BadRequestException("Assignee is not a member of this workspace.");
        }
    }

    /// <summary>
    /// Loads the assignee for the <c>Assignee</c> navigation so <c>MapTask</c> can return
    /// <c>AssigneeName</c>. Always called AFTER <see cref="RequireAssigneeInWorkspaceAsync"/>, so
    /// one query per task write and no N+1.
    /// </summary>
    private Task<ApplicationUser?> LoadAssigneeAsync(Guid? assigneeId, CancellationToken ct)
        => assigneeId is null
            ? Task.FromResult<ApplicationUser?>(null)
            : _db.Users.FirstOrDefaultAsync(u => u.Id == assigneeId.Value, ct);

    /// <summary>
    /// Phase 7 §3.3 — <c>AssigneeIsAiAgent</c> for a whole page, and the labels/comment-count
    /// loaders, now live in <see cref="TaskReadHelpers"/> (Phase 12 §P1) because
    /// <see cref="TaskSearchService"/> renders the same card shape and must not duplicate them.
    /// </summary>
    private async Task<BoardEntity> RequireVisibleBoardAsync(Guid boardId, Guid userId, CancellationToken ct)
    {
        var board = await _db.Boards
            .FirstOrDefaultAsync(b => b.Id == boardId, ct)
            ?? throw new NotFoundException("Board not found.");

        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);
        return board;
    }

    /// <summary>
    /// Authorization check that also returns the task's workspace id so the Phase 5 activity-log
    /// hooks (and the Phase 7 membership check) do not have to load the board a second time.
    /// (A tuple rather than an <c>out</c> parameter — async methods cannot have <c>out</c>
    /// parameters.)
    /// </summary>
    private async Task<Guid> RequireMemberOfTaskBoardWithWorkspaceAsync(
        BoardTask task, Guid userId, CancellationToken ct)
    {
        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == task.BoardId, ct)
            ?? throw new NotFoundException("Task not found.");

        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);
        return board.WorkspaceId;
    }

    private async Task<BoardTask?> LoadTaskAsync(Guid taskId, CancellationToken ct)
        => await _db.Tasks
            .Where(t => t.Id == taskId)
            .Include(t => t.Assignee)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Alerts the new assignee that a task is now theirs (Phase 11 §6.5).
    /// <para>
    /// Skipped when there is no assignee, when the caller assigned themselves (they already know),
    /// and when the assignee is the <b>AI Agent</b> — a pseudo-member with no inbox, whose
    /// "something happened to my run" alerts already travel to the Managers through
    /// <c>AgentNotificationTypes</c>. Sending this one would only create rows nobody reads.
    /// </para>
    /// </summary>
    private async Task NotifyAssignmentAsync(
        Guid workspaceId, BoardTask task, Guid? assigneeId, Guid actorId, CancellationToken ct)
    {
        if (assigneeId is not { } target || target == actorId)
        {
            return;
        }

        // Agent identity is a per-workspace fact (one agent row per workspace), so it cannot be
        // decided from the id alone.
        if (await _agents.IsAiAgentAsync(workspaceId, target, ct))
        {
            return;
        }

        await _notifications.NotifyAsync(
            new MemberNotification(
                workspaceId,
                [target],
                MemberNotificationTypes.TaskAssigned,
                "Bạn được giao một thẻ mới",
                MemberNotificationLimits.Clamp(task.Title, MemberNotificationLimits.Title),
                JsonSerializer.Serialize(
                    new { boardId = task.BoardId, taskId = task.Id, columnId = task.ColumnId },
                    ActivityJson)),
            ct);
    }

    private static TaskPriority? ParsePriority(string? value)
    {
        if (value is null)
        {
            return null;
        }

        // Enum.TryParse alone is NOT a validator: it also parses a NUMERIC string as the enum member
        // at that index — "1" → Medium, "99" → the undefined (TaskPriority)99. Pure names are
        // unaffected ("Urgent", "urgent"), but the wire contract is the four names in DB design §4, so
        // a numeric value must be rejected. Without the IsNumericString guard "1" silently stored
        // Medium; without the IsDefined guard an out-of-range number would sail through and surface as
        // a CHECK-constraint violation (500). Phase 10 §1 caught both via
        // TaskFieldsApiTests.UpdateTask_WithAnUnknownPriority_Returns400.
        return !IsNumericString(value)
               && Enum.TryParse<TaskPriority>(value, ignoreCase: true, out var priority)
               && Enum.IsDefined(priority)
            ? priority
            : throw new BadRequestException(
                $"Priority must be one of: Low, Medium, High, Urgent.");
    }

    /// <summary>
    /// True when <paramref name="value"/> is only a sign and digits — the shape
    /// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> treats as a numeric value rather
    /// than a member name. Deliberately narrow: it must not classify a member name such as
    /// <c>"Urgent"</c> as numeric.
    /// </summary>
    private static bool IsNumericString(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var digits = 0;
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (c is >= '0' and <= '9')
            {
                digits++;
                continue;
            }

            // A sign is only meaningful at the very front.
            if (c is '-' or '+' && i == 0)
            {
                continue;
            }

            return false;
        }

        return digits > 0;
    }

    /// <summary>
    /// Npgsql refuses a <see cref="DateTimeOffset"/> whose offset is not zero when writing to
    /// PostgreSQL <c>timestamptz</c> ("only offset 0 (UTC) is supported"), and it throws from
    /// <c>SaveChangesAsync</c> — after the request has been accepted, so the client sees a 500.
    /// <para>
    /// A client is perfectly entitled to send an ISO-8601 instant with an offset
    /// (<c>2026-06-15T09:00:00+07:00</c>), which is what any non-UTC browser or a Flutter client
    /// would send. The value is an <b>instant</b>, so normalizing it to UTC preserves the meaning and
    /// keeps the database column canonical. Phase 10 §1 caught this with
    /// <c>TaskFieldsApiTests.UpdateTask_WithAnOffsetDueDate_RoundTripsTheSameInstant</c>.
    /// </para>
    /// </summary>
    private static DateTimeOffset? ToUtc(DateTimeOffset? value)
        => value?.ToUniversalTime();

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
