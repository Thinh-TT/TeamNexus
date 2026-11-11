using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Appliers;

/// <summary>
/// Applier for <see cref="AiActionTypes.GitHubPrMoveTask"/> (Phase 15.3 §T15.3.3).
/// Allows workspace managers to cleanly Undo automated task movements caused by GitHub Pull Requests.
/// </summary>
public sealed class GitHubPrMoveTaskApplier : IAiActionApplier
{
    private readonly TeamNexusDbContext _db;
    private readonly ITaskService _tasks;
    private readonly ILogger<GitHubPrMoveTaskApplier> _logger;

    public GitHubPrMoveTaskApplier(
        TeamNexusDbContext db,
        ITaskService tasks,
        ILogger<GitHubPrMoveTaskApplier> logger)
    {
        _db = db;
        _tasks = tasks;
        _logger = logger;
    }

    public string ActionType => AiActionTypes.GitHubPrMoveTask;

    public async Task<AiActionAppliedResult> ApplyAsync(
        AiActionLog log,
        AiActionContext ctx,
        CancellationToken ct = default)
    {
        var boardId = ctx.RequireBoardId();

        var taskId = AiActionService.ReadGuid(log.AfterSnapshot, "taskId")
                     ?? AiActionService.ReadGuid(log.AppliedSnapshot, "updatedTaskId")
                     ?? throw new BadRequestException("Hành động di chuyển PR thiếu mã công việc (taskId).");

        var toColumnId = AiActionService.ReadGuid(log.AfterSnapshot, "columnId")
                         ?? AiActionService.ReadGuid(log.AppliedSnapshot, "toColumnId")
                         ?? throw new BadRequestException("Hành động di chuyển PR thiếu cột đích (columnId).");

        var current = await _tasks.GetTaskByIdAsync(taskId, ctx.ActingUserId, ct);
        if (current.BoardId != boardId)
        {
            throw new BadRequestException("Thẻ không thuộc bảng hiện tại.");
        }

        var fromColumnId = current.ColumnId;
        var targetPosition = await _db.Tasks
            .AsNoTracking()
            .CountAsync(t => t.ColumnId == toColumnId, ct);

        await _tasks.MoveTaskAsync(
            taskId,
            new MoveTaskRequest(toColumnId, targetPosition),
            ctx.ActingUserId,
            ct);

        return new AiActionAppliedResult(
            AiEntityTypes.Board,
            boardId,
            CreatedTaskIds: [],
            CreatedLabelIds: [],
            Warnings: [],
            UpdatedTaskId: taskId,
            FromColumnId: fromColumnId);
    }

    public async Task<IReadOnlyList<string>> UndoAsync(
        AiActionLog log,
        AiActionContext ctx,
        CancellationToken ct = default)
    {
        var warnings = new List<string>();

        var taskId = AiActionService.ReadGuid(log.AppliedSnapshot, "updatedTaskId")
                     ?? AiActionService.ReadGuid(log.BeforeSnapshot, "taskId");

        var fromColumnId = AiActionService.ReadGuid(log.AppliedSnapshot, "fromColumnId")
                           ?? AiActionService.ReadGuid(log.BeforeSnapshot, "columnId");

        if (taskId is null || fromColumnId is null)
        {
            warnings.Add("Không xác định được thẻ hoặc cột ban đầu để hoàn tác di chuyển từ GitHub PR.");
            return warnings;
        }

        // The PR move recorded where the card sat and which completion stamp it carried; restoring both is
        // what makes Undo actually restore the previous state instead of appending the card to the bottom of
        // its old column (review F8).
        var previousPosition = AiActionService.ReadInt(log.AppliedSnapshot, "previousPosition")
                               ?? AiActionService.ReadInt(log.BeforeSnapshot, "position");
        var previousCompletedAt = AiActionService.ReadDateTimeOffset(log.AppliedSnapshot, "previousCompletedAt")
                                  ?? AiActionService.ReadDateTimeOffset(log.BeforeSnapshot, "completedAt");

        try
        {
            var targetColumnExists = await _db.BoardColumns
                .AsNoTracking()
                .AnyAsync(c => c.Id == fromColumnId.Value, ct);

            if (!targetColumnExists)
            {
                warnings.Add("Cột ban đầu đã bị xoá nên không thể hoàn tác di chuyển thẻ.");
                return warnings;
            }

            var otherTaskCount = await _db.Tasks
                .AsNoTracking()
                .CountAsync(t => t.ColumnId == fromColumnId.Value && t.Id != taskId.Value, ct);

            // Clamp: the column may have grown/shrunk since the move, and a position beyond the end would
            // make the board reorder more than this card.
            var position = previousPosition.HasValue
                ? Math.Clamp(previousPosition.Value, 0, otherTaskCount)
                : otherTaskCount;

            await _tasks.MoveTaskAsync(
                taskId.Value,
                new MoveTaskRequest(fromColumnId.Value, position),
                ctx.ActingUserId,
                ct);

            var currentCompletedAt = await _db.Tasks
                .AsNoTracking()
                .Where(t => t.Id == taskId.Value)
                .Select(t => t.CompletedAt)
                .FirstOrDefaultAsync(ct);

            if (currentCompletedAt != previousCompletedAt)
            {
                // MoveTaskAsync derives CompletedAt from the destination column; the snapshot is the truth
                // for an undo, so write the recorded value back when the two disagree.
                await _db.Tasks
                    .Where(t => t.Id == taskId.Value)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(t => t.CompletedAt, previousCompletedAt),
                        ct);
            }

            _logger.LogInformation(
                "GitHub PR move task undone (log {LogId}, task {TaskId}) reverted to column {FromColumnId} at position {Position} by user {UserId}.",
                log.Id,
                taskId.Value,
                fromColumnId.Value,
                position,
                ctx.ActingUserId);
        }
        catch (NotFoundException)
        {
            warnings.Add("Thẻ đã bị xoá nên không thể hoàn tác di chuyển.");
        }
        catch (BadRequestException ex)
        {
            warnings.Add($"Không thể hoàn tác di chuyển: {ex.Message}");
        }

        return warnings;
    }
}
