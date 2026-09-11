using Microsoft.EntityFrameworkCore;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>Read access to agent-produced files (Phase 7 §4.9 routes 6–7).</summary>
public interface IAgentAttachmentService
{
    /// <summary>Attachments of one task, newest first (Member+).</summary>
    Task<IReadOnlyList<AttachmentResponse>> ListAsync(
        Guid taskId, Guid userId, CancellationToken ct = default);

    /// <summary>One attachment with its bytes, ready for <c>Results.File</c> (Member+).</summary>
    Task<(byte[] Content, string ContentType, string FileName)> DownloadAsync(
        Guid taskId, Guid attachmentId, Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Reads <c>task_attachments</c>. Kept out of the endpoints so authorization (workspace membership via
/// the task's board) and the "attachment must belong to THIS task" check live in one place, and the
/// handler stays a thin binder — the Reporting module's export route is the precedent.
/// </summary>
public sealed class AgentAttachmentService : IAgentAttachmentService
{
    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;

    public AgentAttachmentService(TeamNexusDbContext db, IWorkspaceAccess access)
    {
        _db = db;
        _access = access;
    }

    public async Task<IReadOnlyList<AttachmentResponse>> ListAsync(
        Guid taskId, Guid userId, CancellationToken ct = default)
    {
        await RequireVisibleTaskAsync(taskId, userId, ct);

        var attachments = await _db.TaskAttachments
            .AsNoTracking()
            .Where(a => a.TaskId == taskId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        var names = await LoadUserNamesAsync(attachments.Select(a => a.CreatedByUserId), ct);

        return attachments
            .Select(a => AttachmentResponse.From(a, names.GetValueOrDefault(a.CreatedByUserId)))
            .ToList();
    }

    public async Task<(byte[] Content, string ContentType, string FileName)> DownloadAsync(
        Guid taskId, Guid attachmentId, Guid userId, CancellationToken ct = default)
    {
        await RequireVisibleTaskAsync(taskId, userId, ct);

        // Scoped to the task as well as the id: an attachment of another task must look like it does
        // not exist, so a valid id cannot be used to probe across tasks.
        var attachment = await _db.TaskAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.TaskId == taskId, ct)
            ?? throw new NotFoundException("Attachment not found.");

        return (attachment.Content, attachment.ContentType, attachment.FileName);
    }

    private async Task RequireVisibleTaskAsync(Guid taskId, Guid userId, CancellationToken ct)
    {
        var task = await _db.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == task.BoardId, ct)
            ?? throw new NotFoundException("Board not found.");

        // 404 (not 403) when the caller is not a member: the workspace must not be probed.
        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);
    }

    private async Task<Dictionary<Guid, string>> LoadUserNamesAsync(
        IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await _db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
    }
}
