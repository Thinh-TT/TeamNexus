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

    public CommentService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IBoardEventPublisher events,
        IActivityLogWriter activityLog)
    {
        _db = db;
        _access = access;
        _events = events;
        _activityLog = activityLog;
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

        return response;
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
