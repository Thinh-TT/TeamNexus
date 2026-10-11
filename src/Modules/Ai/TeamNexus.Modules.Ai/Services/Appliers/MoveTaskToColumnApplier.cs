using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Appliers;

/// <summary>
/// Applier for <see cref="AiActionTypes.MoveTaskToColumn"/> (Phase 14.5 §B): the board chat may propose
/// moving an attached task to another column, and this writes it on approval.
/// <para>
/// <b>Why this is safe to add.</b> Moving a task is the one write whose whole effect is captured by
/// <c>before_snapshot</c> — the task's previous column — so Undo is a single, exact inverse rather than a
/// reconstruction. It also cannot lose data the way a field update can.
/// </para>
/// <para>
/// <b>Column and board are re-checked here.</b> The validator already rejected a foreign column at submit
/// time; the applier repeats it because between submit and approve a column can be deleted or the board
/// can be edited, and because <c>after_snapshot</c> is caller-supplied JSON. The Board service checks the
/// task's membership and broadcasts the move, so a direct row update would leave every other client
/// showing the card in the wrong column.
/// </para>
/// </summary>
public sealed class MoveTaskToColumnApplier : IAiActionApplier
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TeamNexusDbContext _db;
    private readonly ITaskService _tasks;
    private readonly ILogger<MoveTaskToColumnApplier> _logger;

    public MoveTaskToColumnApplier(
        TeamNexusDbContext db,
        ITaskService tasks,
        ILogger<MoveTaskToColumnApplier> logger)
    {
        _db = db;
        _tasks = tasks;
        _logger = logger;
    }

    public string ActionType => AiActionTypes.MoveTaskToColumn;

    public async Task<AiActionAppliedResult> ApplyAsync(
        AiActionLog log, AiActionContext ctx, CancellationToken ct = default)
    {
        var boardId = ctx.RequireBoardId();
        var proposal = ParseProposal(log);

        var taskId = proposal.TaskId
            ?? throw new BadRequestException("Đề xuất di chuyển thiếu thẻ cần thao tác (taskId).");

        var columnId = proposal.ColumnId
            ?? throw new BadRequestException("Đề xuất di chuyển thiếu cột đích (columnId).");

        var current = await _tasks.GetTaskByIdAsync(taskId, ctx.ActingUserId, ct);

        if (current.BoardId != boardId)
        {
            throw new BadRequestException("Thẻ cần di chuyển không thuộc bảng này.");
        }

        // Re-read the target column rather than trusting `after_snapshot`: a column deleted between submit
        // and approve must be a clean 400, not a move into a column that no longer exists.
        var targetExists = await _db.BoardColumns
            .AsNoTracking()
            .AnyAsync(c => c.Id == columnId && c.BoardId == boardId, ct);

        if (!targetExists)
        {
            throw new BadRequestException("Cột đích không thuộc bảng này hoặc đã bị xoá.");
        }

        var fromColumnId = current.ColumnId;

        // Append to the end of the target column. `MoveTaskAsync` takes an explicit position, and the
        // chat has no opinion about ordering — placing the card last is the least surprising default and
        // matches what creating a task in that column would do.
        var targetPosition = await _db.Tasks
            .AsNoTracking()
            .CountAsync(t => t.ColumnId == columnId, ct);

        await _tasks.MoveTaskAsync(taskId, new MoveTaskRequest(columnId, targetPosition), ctx.ActingUserId, ct);

        _logger.LogInformation(
            "MoveTaskToColumn applied (log {LogId}, task {TaskId}) from column {FromColumnId} to "
            + "{ToColumnId} by user {UserId}.",
            log.Id, taskId, fromColumnId, columnId, ctx.ActingUserId);

        return new AiActionAppliedResult(
            AiEntityTypes.Task,
            taskId,
            CreatedTaskIds: [],
            CreatedLabelIds: [],
            Warnings: [],
            UpdatedTaskId: taskId,
            FromColumnId: fromColumnId);
    }

    public async Task<IReadOnlyList<string>> UndoAsync(
        AiActionLog log, AiActionContext ctx, CancellationToken ct = default)
    {
        var warnings = new List<string>();

        var taskId = AiActionService.ReadGuid(log.AppliedSnapshot, "updatedTaskId")
                     ?? ParseProposal(log).TaskId;

        var fromColumnId = AiActionService.ReadGuid(log.AppliedSnapshot, "fromColumnId");

        if (taskId is null || fromColumnId is null)
        {
            warnings.Add("Không xác định được cột trước khi di chuyển nên không thể hoàn tác.");
            return warnings;
        }

        try
        {
            var position = await _db.Tasks
                .AsNoTracking()
                .CountAsync(t => t.ColumnId == fromColumnId.Value && t.Id != taskId.Value, ct);

            await _tasks.MoveTaskAsync(
                taskId.Value,
                new MoveTaskRequest(fromColumnId.Value, position),
                ctx.ActingUserId,
                ct);
        }
        catch (NotFoundException)
        {
            warnings.Add("Thẻ đã bị xoá nên không thể hoàn tác di chuyển.");
            return warnings;
        }
        catch (BadRequestException)
        {
            // The original column was deleted by a human in the meantime. Reported, not thrown: the rest
            // of the Undo (and the log transition) has already happened by the time appliers run.
            warnings.Add("Cột cũ đã bị xoá nên không thể hoàn tác di chuyển.");
            return warnings;
        }

        _logger.LogInformation(
            "MoveTaskToColumn undone (log {LogId}, task {TaskId}) back to column {ColumnId}.",
            log.Id, taskId.Value, fromColumnId.Value);

        return warnings;
    }

    // ---- snapshot shapes ---------------------------------------------------

    private sealed record ProposalSnapshot(Guid? TaskId, Guid? ColumnId);

    private static ProposalSnapshot ParseProposal(AiActionLog log)
    {
        if (string.IsNullOrWhiteSpace(log.AfterSnapshot))
        {
            throw new BadRequestException("Đề xuất di chuyển thẻ không có nội dung.");
        }

        try
        {
            return JsonSerializer.Deserialize<ProposalSnapshot>(log.AfterSnapshot, Json)
                   ?? throw new BadRequestException("Đề xuất di chuyển thẻ rỗng.");
        }
        catch (JsonException ex)
        {
            throw new BadRequestException($"Đề xuất di chuyển thẻ không phải JSON hợp lệ: {ex.Message}");
        }
    }
}
