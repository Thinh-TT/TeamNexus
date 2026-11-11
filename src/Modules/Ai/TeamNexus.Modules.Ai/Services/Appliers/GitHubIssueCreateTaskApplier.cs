using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;
using TeamNexus.Shared;

namespace TeamNexus.Modules.Ai.Services.Appliers;

/// <summary>
/// Applier for <see cref="AiActionTypes.GitHubIssueCreateTask"/> (Phase 15.4 §T15.4.2).
/// Allows workspace managers to cleanly Undo automated task creations triggered by GitHub Issues.
/// </summary>
public sealed class GitHubIssueCreateTaskApplier : IAiActionApplier
{
    private readonly TeamNexusDbContext _db;
    private readonly ITaskService _tasks;
    private readonly ILogger<GitHubIssueCreateTaskApplier> _logger;

    public GitHubIssueCreateTaskApplier(
        TeamNexusDbContext db,
        ITaskService tasks,
        ILogger<GitHubIssueCreateTaskApplier> logger)
    {
        _db = db;
        _tasks = tasks;
        _logger = logger;
    }

    public string ActionType => AiActionTypes.GitHubIssueCreateTask;

    public Task<AiActionAppliedResult> ApplyAsync(
        AiActionLog log,
        AiActionContext ctx,
        CancellationToken ct = default)
    {
        var boardId = ctx.RequireBoardId();
        var taskIds = AiActionService.ReadGuidArray(log.AppliedSnapshot, "createdTaskIds");
        if (taskIds.Count == 0)
        {
            taskIds = AiActionService.ReadGuidArray(log.AfterSnapshot, "createdTaskIds");
        }

        return Task.FromResult(new AiActionAppliedResult(
            AiEntityTypes.Board,
            boardId,
            CreatedTaskIds: taskIds,
            CreatedLabelIds: [],
            Warnings: []));
    }

    public async Task<IReadOnlyList<string>> UndoAsync(
        AiActionLog log,
        AiActionContext ctx,
        CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var taskIds = AiActionService.ReadGuidArray(log.AppliedSnapshot, "createdTaskIds");
        if (taskIds.Count == 0)
        {
            taskIds = AiActionService.ReadGuidArray(log.AfterSnapshot, "createdTaskIds");
        }

        if (taskIds.Count == 0)
        {
            warnings.Add("Không tìm thấy mã thẻ nào trong bản ghi để hoàn tác tạo thẻ từ GitHub Issue.");
            return warnings;
        }

        var activeTaskIds = await _db.Tasks
            .IgnoreQueryFilters()
            .Where(t => taskIds.Contains(t.Id) && t.DeletedAt == null)
            .Select(t => t.Id)
            .ToListAsync(ct);

        foreach (var taskId in activeTaskIds)
        {
            try
            {
                await _tasks.DeleteTaskAsync(taskId, ctx.ActingUserId, ct);
            }
            catch (NotFoundException)
            {
                warnings.Add($"Thẻ {taskId} đã bị xóa từ trước.");
            }
            catch (Exception ex)
            {
                warnings.Add($"Không thể hoàn tác xóa thẻ {taskId}: {ex.Message}");
            }
        }

        var externalLinks = await _db.TaskExternalLinks
            .Where(l => taskIds.Contains(l.TaskId))
            .ToListAsync(ct);

        if (externalLinks.Count > 0)
        {
            _db.TaskExternalLinks.RemoveRange(externalLinks);
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "GitHub Issue create task undone (log {LogId}, board {BoardId}): {Count} task(s) soft-deleted by user {UserId}.",
            log.Id,
            ctx.RequireBoardId(),
            activeTaskIds.Count,
            ctx.ActingUserId);

        return warnings;
    }
}
