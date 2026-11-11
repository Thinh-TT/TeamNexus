using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;
using TeamNexus.Shared;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Implementation of <see cref="ITaskAutomation"/> (Phase 15.1 §T15.1.5).
/// </summary>
public sealed class TaskAutomationService : ITaskAutomation
{
    private const int MaxTitleLength = 200;
    private const int MaxDescriptionLength = 2000;
    private const int MaxCommentLength = 4000;

    private static readonly string[] LabelPalette =
        ["#4F46E5", "#10B981", "#F59E0B", "#EF4444", "#8B5CF6", "#06B6D4"];

    private static readonly JsonSerializerOptions ActivityJson = new(JsonSerializerDefaults.Web);

    private readonly TeamNexusDbContext _db;
    private readonly IBoardEventPublisher _events;
    private readonly IActivityLogWriter _activityLog;
    private readonly IAiAgentResolver _agents;

    public TaskAutomationService(
        TeamNexusDbContext db,
        IBoardEventPublisher events,
        IActivityLogWriter activityLog,
        IAiAgentResolver agents)
    {
        _db = db;
        _events = events;
        _activityLog = activityLog;
        _agents = agents;
    }

    public async Task<TaskAutomationMoveResult> MoveToSystemColumnAsync(
        Guid taskId,
        SystemColumnTarget target,
        string reason,
        CancellationToken ct = default)
    {
        var task = await _db.Tasks
            .Include(t => t.Assignee)
            .Include(t => t.Board)
            .Include(t => t.Column)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        var board = task.Board ?? throw new NotFoundException("Board not found.");
        var fromColumnId = task.ColumnId;
        var previousPosition = task.Position;
        var previousCompletedAt = task.CompletedAt;

        // If moving to InReview and the task is already in Done or InReview, skip gracefully.
        if (target == SystemColumnTarget.InReview
            && task.Column is not null
            && (task.Column.IsDone || task.Column.IsInReview))
        {
            return new TaskAutomationMoveResult(
                Moved: false,
                TaskId: task.Id,
                BoardId: task.BoardId,
                WorkspaceId: board.WorkspaceId,
                FromColumnId: fromColumnId,
                ToColumnId: fromColumnId,
                PreviousPosition: previousPosition,
                NewPosition: previousPosition,
                PreviousCompletedAt: previousCompletedAt,
                NewCompletedAt: previousCompletedAt,
                SkippedReason: task.Column.IsDone ? "already_done" : "already_in_review");
        }

        if (target == SystemColumnTarget.Done
            && task.Column is not null
            && task.Column.IsDone)
        {
            return new TaskAutomationMoveResult(
                Moved: false,
                TaskId: task.Id,
                BoardId: task.BoardId,
                WorkspaceId: board.WorkspaceId,
                FromColumnId: fromColumnId,
                ToColumnId: fromColumnId,
                PreviousPosition: previousPosition,
                NewPosition: previousPosition,
                PreviousCompletedAt: previousCompletedAt,
                NewCompletedAt: previousCompletedAt,
                SkippedReason: "already_done");
        }

        var targetColumn = target switch
        {
            SystemColumnTarget.InReview => await _db.BoardColumns
                .Where(c => c.BoardId == task.BoardId && c.IsInReview)
                .OrderBy(c => c.Position)
                .FirstOrDefaultAsync(ct),
            SystemColumnTarget.Done => await _db.BoardColumns
                .Where(c => c.BoardId == task.BoardId && c.IsDone)
                .OrderBy(c => c.Position)
                .FirstOrDefaultAsync(ct),
            _ => null,
        };

        if (targetColumn is null)
        {
            return new TaskAutomationMoveResult(
                Moved: false,
                TaskId: task.Id,
                BoardId: task.BoardId,
                WorkspaceId: board.WorkspaceId,
                FromColumnId: fromColumnId,
                ToColumnId: fromColumnId,
                PreviousPosition: previousPosition,
                NewPosition: previousPosition,
                PreviousCompletedAt: previousCompletedAt,
                NewCompletedAt: previousCompletedAt,
                SkippedReason: target == SystemColumnTarget.InReview ? "no_in_review_column" : "no_done_column");
        }

        var tasksInColumn = await _db.Tasks
            .Where(t => t.ColumnId == targetColumn.Id)
            .OrderBy(t => t.Position)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);

        tasksInColumn.Remove(task);
        var insertIndex = tasksInColumn.Count;
        tasksInColumn.Insert(insertIndex, task);

        for (var i = 0; i < tasksInColumn.Count; i++)
        {
            tasksInColumn[i].Position = i;
        }

        task.ColumnId = targetColumn.Id;
        task.CompletedAt = targetColumn.IsDone ? DateTimeOffset.UtcNow : null;

        await _db.SaveChangesAsync(ct);

        var response = await BuildTaskResponseAsync(task, board.WorkspaceId, ct);

        await _events.TaskMoved(
            task.BoardId,
            new TaskMovedEventPayload(task.Id, fromColumnId, targetColumn.Id, insertIndex),
            ct);
        await _events.TaskUpdated(task.BoardId, response, ct);

        await _activityLog.RecordAsync(new ActivityLogEntry(
            board.WorkspaceId,
            task.BoardId,
            UserId: null,
            ObserverEntityTypes.Task,
            task.Id,
            ObserverActivityActions.TaskMoved,
            JsonSerializer.Serialize(
                new
                {
                    fromColumnId,
                    toColumnId = targetColumn.Id,
                    position = insertIndex,
                    reason,
                },
                ActivityJson)), ct);

        if (targetColumn.IsDone)
        {
            await _activityLog.RecordAsync(new ActivityLogEntry(
                board.WorkspaceId,
                task.BoardId,
                UserId: null,
                ObserverEntityTypes.Task,
                task.Id,
                ObserverActivityActions.TaskCompleted,
                JsonSerializer.Serialize(
                    new
                    {
                        columnId = targetColumn.Id,
                        reason,
                    },
                    ActivityJson)), ct);
        }

        return new TaskAutomationMoveResult(
            Moved: true,
            TaskId: task.Id,
            BoardId: task.BoardId,
            WorkspaceId: board.WorkspaceId,
            FromColumnId: fromColumnId,
            ToColumnId: targetColumn.Id,
            PreviousPosition: previousPosition,
            NewPosition: insertIndex,
            PreviousCompletedAt: previousCompletedAt,
            NewCompletedAt: task.CompletedAt,
            Task: response);
    }

    public async Task<TaskResponse> CreateTaskFromExternalIssueAsync(
        Guid boardId,
        string title,
        string? description,
        CancellationToken ct = default)
    {
        var board = await _db.Boards
            .FirstOrDefaultAsync(b => b.Id == boardId, ct)
            ?? throw new NotFoundException("Board not found.");

        var columns = await _db.BoardColumns
            .Where(c => c.BoardId == boardId)
            .OrderBy(c => c.Position)
            .ToListAsync(ct);

        var targetColumn = columns.FirstOrDefault(c => !c.IsDone && !c.IsClarification && !c.IsInReview)
                           ?? columns.FirstOrDefault()
                           ?? throw new BadRequestException("Board has no columns to create a task in.");

        await EnsureSystemUserExistsAsync(SystemUsers.GitHubBotUserId, ct);

        var maxPosition = await _db.Tasks
            .Where(t => t.ColumnId == targetColumn.Id)
            .MaxAsync(t => (int?)t.Position, ct) ?? -1;

        var normalizedTitle = NormalizeTitle(title);
        var normalizedDescription = NormalizeDescription(description);
        var taskId = Guid.NewGuid();
        var taskNumber = await TaskReadHelpers.AllocateNextTaskNumberAsync(_db, board.WorkspaceId, taskId, ct);

        var task = new BoardTask
        {
            Id = taskId,
            BoardId = boardId,
            ColumnId = targetColumn.Id,
            Title = normalizedTitle,
            Description = normalizedDescription,
            Position = maxPosition + 1,
            CreatedBy = SystemUsers.GitHubBotUserId,
            CompletedAt = targetColumn.IsDone ? DateTimeOffset.UtcNow : null,
            TaskNumber = taskNumber,
        };

        _db.Tasks.Add(task);
        await _db.SaveChangesAsync(ct);

        var response = await BuildTaskResponseAsync(task, board.WorkspaceId, ct);
        await _events.TaskCreated(boardId, response, ct);

        await _activityLog.RecordAsync(new ActivityLogEntry(
            board.WorkspaceId,
            boardId,
            UserId: null,
            ObserverEntityTypes.Task,
            task.Id,
            ObserverActivityActions.TaskCreated,
            JsonSerializer.Serialize(
                new
                {
                    columnId = targetColumn.Id,
                    source = "github_issue",
                    taskNumber,
                },
                ActivityJson)), ct);

        return response;
    }

    public async Task<TaskResponse> ApplySystemTriageSuggestionsAsync(
        Guid taskId,
        string? priority,
        decimal? estimatedHours,
        IReadOnlyList<string>? tags,
        Guid? assigneeId,
        CancellationToken ct = default)
    {
        var task = await _db.Tasks
            .Include(t => t.Assignee)
            .Include(t => t.Board)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        var board = task.Board ?? throw new NotFoundException("Board not found.");
        var changed = false;

        if (!string.IsNullOrWhiteSpace(priority)
            && Enum.TryParse<TaskPriority>(priority.Trim(), ignoreCase: true, out var parsedPriority)
            && Enum.IsDefined(parsedPriority))
        {
            task.Priority = parsedPriority;
            changed = true;
        }

        if (assigneeId.HasValue)
        {
            var isMember = await _db.WorkspaceMembers
                .AnyAsync(wm => wm.WorkspaceId == board.WorkspaceId && wm.UserId == assigneeId.Value, ct);
            if (isMember)
            {
                task.AssigneeId = assigneeId.Value;
                task.Assignee = await _db.Users.FirstOrDefaultAsync(u => u.Id == assigneeId.Value, ct);
                changed = true;
            }
        }

        if (tags is { Count: > 0 })
        {
            var distinctTags = tags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Where(t => t.Length <= 50)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (distinctTags.Count > 0)
            {
                var existingLabels = await _db.Labels
                    .Where(l => l.WorkspaceId == board.WorkspaceId)
                    .ToListAsync(ct);

                var existingTaskLabelIds = (await _db.TaskLabels
                    .Where(tl => tl.TaskId == task.Id)
                    .Select(tl => tl.LabelId)
                    .ToListAsync(ct))
                    .ToHashSet();

                foreach (var tagName in distinctTags)
                {
                    var label = existingLabels.FirstOrDefault(
                        l => string.Equals(l.Name, tagName, StringComparison.OrdinalIgnoreCase));

                    if (label is null)
                    {
                        label = new Label
                        {
                            Id = Guid.NewGuid(),
                            WorkspaceId = board.WorkspaceId,
                            Name = tagName,
                            Color = LabelPalette[existingLabels.Count % LabelPalette.Length],
                            CreatedAt = DateTimeOffset.UtcNow,
                        };
                        _db.Labels.Add(label);
                        existingLabels.Add(label);
                    }

                    if (existingTaskLabelIds.Add(label.Id))
                    {
                        _db.TaskLabels.Add(new TaskLabel
                        {
                            TaskId = task.Id,
                            LabelId = label.Id,
                        });
                        changed = true;
                    }
                }
            }
        }

        if (changed)
        {
            await _db.SaveChangesAsync(ct);
        }

        var response = await BuildTaskResponseAsync(task, board.WorkspaceId, ct);
        if (changed)
        {
            await _events.TaskUpdated(task.BoardId, response, ct);
        }

        return response;
    }

    public async Task<CommentResponse> AddSystemCommentAsync(
        Guid taskId,
        Guid systemUserId,
        string markdownContent,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(markdownContent))
        {
            throw new BadRequestException("Comment content cannot be empty.");
        }

        var task = await _db.Tasks
            .Include(t => t.Board)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        var board = task.Board ?? throw new NotFoundException("Board not found.");
        await EnsureSystemUserExistsAsync(systemUserId, ct);

        var trimmed = markdownContent.Trim();
        var content = trimmed.Length <= MaxCommentLength ? trimmed : trimmed[..MaxCommentLength];

        var comment = new TaskComment
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            AuthorId = systemUserId,
            Content = content,
        };

        _db.TaskComments.Add(comment);
        await _db.SaveChangesAsync(ct);

        var authorName = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == systemUserId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct)
            ?? SystemUsers.GitHubBotDisplayName;

        var response = new CommentResponse(
            comment.Id,
            comment.TaskId,
            comment.AuthorId,
            authorName,
            comment.Content,
            comment.CreatedAt,
            comment.UpdatedAt);

        await _events.CommentAdded(task.BoardId, response, ct);

        await _activityLog.RecordAsync(new ActivityLogEntry(
            board.WorkspaceId,
            task.BoardId,
            UserId: null,
            ObserverEntityTypes.Comment,
            comment.Id,
            ObserverActivityActions.CommentAdded,
            JsonSerializer.Serialize(
                new
                {
                    commentId = comment.Id,
                    taskId,
                    source = "github_comment",
                },
                ActivityJson)), ct);

        return response;
    }

    public async Task NotifyTaskUpdatedAsync(Guid taskId, CancellationToken ct = default)
    {
        var task = await _db.Tasks
            .Include(t => t.Board)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);

        if (task?.Board is null)
        {
            // The task was deleted between the link update and the broadcast; nothing to tell the UI.
            return;
        }

        var response = await BuildTaskResponseAsync(task, task.Board.WorkspaceId, ct);
        await _events.TaskUpdated(task.BoardId, response, ct);
    }

    // ---- helpers ----------------------------------------------------------

    private async Task<TaskResponse> BuildTaskResponseAsync(BoardTask task, Guid workspaceId, CancellationToken ct)
    {
        var count = await _db.TaskComments.CountAsync(c => c.TaskId == task.Id, ct);
        var labels = await TaskReadHelpers.LoadLabelsByTaskAsync(_db, [task.Id], ct);
        var externalLinks = await TaskReadHelpers.LoadExternalLinksByTaskAsync(_db, [task.Id], ct);
        var activeRunIds = await AgentRunLookup.LoadActiveRunIdsAsync(_db, [task.Id], ct);
        var assigneeIsAiAgent = await TaskReadHelpers.ResolveAssigneeIsAiAgentAsync(_agents, workspaceId, [task], ct);

        return DtoMapping.MapTask(
            task,
            labels.GetValueOrDefault(task.Id, []),
            count,
            assigneeIsAiAgent,
            activeRunIds.TryGetValue(task.Id, out var runId) ? runId : null,
            externalLinks.GetValueOrDefault(task.Id, []));
    }

    private async Task EnsureSystemUserExistsAsync(Guid systemUserId, CancellationToken ct)
    {
        var exists = await _db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == systemUserId, ct);

        if (exists)
        {
            return;
        }

        var isGitHubBot = systemUserId == SystemUsers.GitHubBotUserId;
        var userName = isGitHubBot ? SystemUsers.GitHubBotUserName : $"system-{systemUserId:N}";
        var email = isGitHubBot ? SystemUsers.GitHubBotEmail : $"system-{systemUserId:N}@system.teamnexus.invalid";
        var displayName = isGitHubBot ? SystemUsers.GitHubBotDisplayName : "System Bot";

        await _db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO users (
                id, user_name, normalized_user_name, email, normalized_email,
                email_confirmed, password_hash, security_stamp, concurrency_stamp,
                phone_number, phone_number_confirmed, two_factor_enabled,
                lockout_end, lockout_enabled, access_failed_count,
                display_name, avatar_url, digest_enabled, is_system, created_at
            )
            VALUES (
                {0}, {1}, {2}, {3}, {4},
                true, NULL, {5}, {5},
                NULL, false, true,
                NULL, true, 0,
                {6}, NULL, false, true, TIMESTAMPTZ '2026-01-01 00:00:00+00'
            )
            ON CONFLICT (id) DO NOTHING;
            """,
            [
                systemUserId,
                userName,
                userName.ToUpperInvariant(),
                email,
                email.ToUpperInvariant(),
                systemUserId.ToString("D"),
                displayName
            ],
            ct);
    }

    private static string NormalizeTitle(string title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
        {
            return "GitHub Issue";
        }

        return trimmed.Length <= MaxTitleLength ? trimmed : trimmed[..MaxTitleLength];
    }

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= MaxDescriptionLength ? trimmed : trimmed[..MaxDescriptionLength];
    }
}

