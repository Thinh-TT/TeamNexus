using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Board.Services;

public interface ICommentService
{
    Task<IReadOnlyList<CommentResponse>> GetCommentsAsync(Guid taskId, Guid userId, CancellationToken ct = default);

    Task<CommentResponse> CreateCommentAsync(Guid taskId, CreateCommentRequest request, Guid userId, CancellationToken ct = default);

    Task<CommentResponse> UpdateCommentAsync(Guid commentId, UpdateCommentRequest request, Guid userId, CancellationToken ct = default);

    Task DeleteCommentAsync(Guid commentId, Guid userId, CancellationToken ct = default);
}

public sealed class CommentService : ICommentService
{
    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IBoardEventPublisher _events;
    private readonly IActivityLogWriter _activityLog;
    private readonly IAiAgentResolver _agents;
    private readonly INotificationWriter _notifications;

    public CommentService(
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

    public async Task<IReadOnlyList<CommentResponse>> GetCommentsAsync(
        Guid taskId, Guid userId, CancellationToken ct = default)
    {
        var task = await LoadTaskWithBoardAsync(taskId, ct);
        await _access.RequireMemberAsync(task.Board!.WorkspaceId, userId, ct);

        var comments = await _db.TaskComments
            .Where(c => c.TaskId == taskId)
            .Include(c => c.Author)
            .OrderBy(c => c.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);

        return comments.Select(ToResponse).ToList();
    }

    public async Task<CommentResponse> CreateCommentAsync(
        Guid taskId, CreateCommentRequest request, Guid userId, CancellationToken ct = default)
    {
        var task = await LoadTaskWithBoardAsync(taskId, ct);
        await _access.RequireMemberAsync(task.Board!.WorkspaceId, userId, ct);
        ValidateContent(request.Content);

        // Validate the mentioned ids BEFORE writing anything: a comment that names somebody who cannot
        // be mentioned must not exist at all, or the workspace is left with a comment whose alert never
        // arrived (Phase 12 §3.4 step 2).
        var mentioned = await ResolveMentionedMembersAsync(
            task.Board.WorkspaceId, request.MentionUserIds, ct);

        var comment = new TaskComment
        {
            TaskId = taskId,
            AuthorId = userId,
            Content = request.Content.Trim(),
        };

        _db.TaskComments.Add(comment);
        await _db.SaveChangesAsync(ct);

        var response = await LoadCommentAsync(comment.Id, ct);
        await _events.CommentAdded(task.BoardId, response, ct);

        // Phase 5 §2.3: comments are the Observer's "communication context" signal source
        // (metadata only — the content is never copied into the log).
        await _activityLog.RecordAsync(new ActivityLogEntry(
            task.Board!.WorkspaceId,
            task.BoardId,
            userId,
            ObserverEntityTypes.Comment,
            comment.Id,
            ObserverActivityActions.CommentAdded,
            JsonSerializer.Serialize(
                new { commentId = comment.Id, taskId },
                ActivityJson)), ct);

        // Phase 11 §6.6: tell the person the task belongs to. Only the assignee — fanning out to the
        // whole workspace would be noise.
        await NotifyAssigneeAsync(task, comment, userId, ct);

        // Phase 12 §3.4: the people explicitly tagged. Runs after the assignee alert so the two can
        // never notify the same person twice.
        await NotifyMentionedAsync(task, comment, mentioned, userId, ct);

        return response;
    }

    /// <summary>
    /// One alert for the task's assignee (Phase 11 §6.6).
    /// <para>
    /// Skipped when the task has no assignee, when the commenter <i>is</i> the assignee (nobody needs
    /// an alert about their own comment), and when the assignee is the <b>AI Agent</b> — it has no
    /// inbox, and it is explicitly woken by a human pressing "Chạy lại" rather than by a notification
    /// (Phase 7 D9: a comment must never auto-trigger a run).
    /// </para>
    /// </summary>
    private async Task NotifyAssigneeAsync(
        BoardTask task, TaskComment comment, Guid actorId, CancellationToken ct)
    {
        if (task.AssigneeId is not { } assigneeId || assigneeId == actorId)
        {
            return;
        }

        var workspaceId = task.Board!.WorkspaceId;

        if (await _agents.IsAiAgentAsync(workspaceId, assigneeId, ct))
        {
            return;
        }

        var authorName = comment.Author?.DisplayName ?? "Ai đó";
        var excerpt = MemberNotificationLimits.Clamp(comment.Content, MemberNotificationLimits.CommentExcerpt);

        await _notifications.NotifyAsync(
            new MemberNotification(
                workspaceId,
                [assigneeId],
                MemberNotificationTypes.CommentOnTask,
                "Có bình luận mới trên thẻ của bạn",
                // The comment body is a preview here, never copied in full (Phase 5 §2.3 keeps text
                // content out of the append-only stores; the notification is a pointer to the task).
                $"{authorName}: {excerpt}",
                JsonSerializer.Serialize(
                    new { boardId = task.BoardId, taskId = task.Id, commentId = comment.Id },
                    ActivityJson)),
            ct);
    }

    /// <summary>
    /// Validates and de-duplicates the ids the comment named (Phase 12 §3.4).
    /// <para>
    /// <b>Human members only.</b> The AI Agent is a <c>workspace_members</c> row with no inbox, and
    /// mentioning it would produce an alert nobody reads (the same exclusion
    /// <see cref="NotifyAssigneeAsync"/> makes). A user outside the workspace is a <b>400</b>: they
    /// cannot even see the task, so an alert about it would leak the existence of work they have no
    /// access to.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveMentionedMembersAsync(
        Guid workspaceId, IReadOnlyList<Guid>? mentionUserIds, CancellationToken ct)
    {
        if (mentionUserIds is null || mentionUserIds.Count == 0)
        {
            return [];
        }

        var requested = mentionUserIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (requested.Count == 0)
        {
            return [];
        }

        if (requested.Count > MemberNotificationLimits.MaxMentionedUsers)
        {
            throw new BadRequestException(
                $"A comment can mention at most {MemberNotificationLimits.MaxMentionedUsers} people.");
        }

        var valid = await _db.WorkspaceMembers
            .Where(wm => wm.WorkspaceId == workspaceId
                         && requested.Contains(wm.UserId)
                         && wm.MemberType == MemberType.Human)
            .Select(wm => wm.UserId)
            .ToListAsync(ct);

        if (valid.Count != requested.Count)
        {
            throw new BadRequestException(
                "Mentioned users must be members of this workspace (and cannot be the AI Agent).");
        }

        return valid;
    }

    /// <summary>
    /// Alerts the people a comment explicitly tagged (Phase 12 §3.4).
    /// <para>
    /// The author is dropped (nobody needs to be told about their own sentence) and so is anyone who
    /// already received the assignee alert above — one comment must never produce two rows for the same
    /// person, because the bell would then count the same event twice.
    /// </para>
    /// </summary>
    private async Task NotifyMentionedAsync(
        BoardTask task,
        TaskComment comment,
        IReadOnlyList<Guid> mentioned,
        Guid actorId,
        CancellationToken ct)
    {
        if (mentioned.Count == 0)
        {
            return;
        }

        var recipients = mentioned
            .Where(id => id != actorId && id != task.AssigneeId)
            .ToList();

        if (recipients.Count == 0)
        {
            return;
        }

        var authorName = comment.Author?.DisplayName ?? "Ai đó";
        var excerpt = MemberNotificationLimits.Clamp(comment.Content, MemberNotificationLimits.CommentExcerpt);

        await _notifications.NotifyAsync(
            new MemberNotification(
                task.Board!.WorkspaceId,
                recipients,
                MemberNotificationTypes.CommentMention,
                "Bạn được nhắc đến trong một bình luận",
                // A preview, never the full body: the notification is a pointer to the task.
                $"{authorName}: {excerpt}",
                JsonSerializer.Serialize(
                    new
                    {
                        boardId = task.BoardId,
                        taskId = task.Id,
                        commentId = comment.Id,
                        mentionedCount = recipients.Count,
                    },
                    ActivityJson)),
            ct);
    }

    public async Task<CommentResponse> UpdateCommentAsync(
        Guid commentId, UpdateCommentRequest request, Guid userId, CancellationToken ct = default)
    {
        var comment = await LoadCommentForAuthorizeAsync(commentId, ct);
        await AuthorizeAsync(comment, userId, ct);
        ValidateContent(request.Content);

        comment.Content = request.Content.Trim();
        await _db.SaveChangesAsync(ct);

        return await LoadCommentAsync(commentId, ct);
    }

    public async Task DeleteCommentAsync(Guid commentId, Guid userId, CancellationToken ct = default)
    {
        var comment = await LoadCommentForAuthorizeAsync(commentId, ct);
        await AuthorizeAsync(comment, userId, ct);

        var boardId = comment.Task?.BoardId;
        comment.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (boardId.HasValue)
        {
            await _events.CommentDeleted(boardId.Value, commentId, comment.TaskId, ct);
        }
    }

    // ---- helpers ----------------------------------------------------------

    /// <summary>camelCase JSON for activity payloads (Phase 5 §2, decision A6).</summary>
    private static readonly JsonSerializerOptions ActivityJson = new(JsonSerializerDefaults.Web);

    private async Task<BoardTask> LoadTaskWithBoardAsync(Guid taskId, CancellationToken ct)
        => await _db.Tasks
            .Where(t => t.Id == taskId)
            .Include(t => t.Board)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Task not found.");

    private async Task<CommentResponse> LoadCommentAsync(Guid commentId, CancellationToken ct)
    {
        var comment = await _db.TaskComments
            .Where(c => c.Id == commentId)
            .Include(c => c.Author)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Comment not found.");

        return ToResponse(comment);
    }

    /// <summary>Comment + task + board for authorization (author or manager of the board's workspace).</summary>
    private async Task<TaskComment> LoadCommentForAuthorizeAsync(Guid commentId, CancellationToken ct)
        => await _db.TaskComments
            .Where(c => c.Id == commentId)
            .Include(c => c.Task!)
            .ThenInclude(t => t.Board)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Comment not found.");

    private async Task AuthorizeAsync(TaskComment comment, Guid userId, CancellationToken ct)
    {
        if (comment.AuthorId == userId)
        {
            return;
        }

        var board = comment.Task?.Board;
        if (board is null)
        {
            throw new NotFoundException("Comment not found.");
        }

        // Manager/Admin of the task's workspace may edit/delete any comment.
        await _access.RequireManagerAsync(board.WorkspaceId, userId, ct);
    }

    private static CommentResponse ToResponse(TaskComment comment)
        => new(
            comment.Id,
            comment.TaskId,
            comment.AuthorId,
            comment.Author?.DisplayName ?? string.Empty,
            comment.Content,
            comment.CreatedAt,
            comment.UpdatedAt);

    private static void ValidateContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Trim().Length > 4000)
        {
            throw new BadRequestException("Comment content must be 1–4000 characters.");
        }
    }
}
