using System.Text.Json;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Appliers;

/// <summary>
/// Applier for <see cref="AiActionTypes.CreateBoardFromTemplate"/> (Phase 14 §6, decisions D12/D13/D14):
/// turns an approved board proposal into a real board, its columns and its starter tasks.
/// <para>
/// <b>Scope is the workspace, not a board</b> — this is the one action that <i>creates</i> its target, so
/// <c>ctx.BoardId</c> is deliberately null and the new board's id is written into
/// <c>applied_snapshot.createdBoardId</c> for Undo.
/// </para>
/// <para>
/// <b>Everything goes through the Board module's own services</b> (<c>IBoardService</c>,
/// <c>IColumnService</c>, <c>ITaskService</c>) rather than raw EF writes: that is what buys the audit
/// rows, the SignalR broadcasts and the permission re-checks for free, exactly as
/// <c>CreateSubtasksApplier</c> does. The acting user is the Manager who approved the action.
/// </para>
/// </summary>
public sealed class CreateBoardFromTemplateApplier : IAiActionApplier
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IBoardService _boards;
    private readonly IColumnService _columns;
    private readonly ITaskService _tasks;
    private readonly ILogger<CreateBoardFromTemplateApplier> _logger;

    public CreateBoardFromTemplateApplier(
        IBoardService boards,
        IColumnService columns,
        ITaskService tasks,
        ILogger<CreateBoardFromTemplateApplier> logger)
    {
        _boards = boards;
        _columns = columns;
        _tasks = tasks;
        _logger = logger;
    }

    public string ActionType => AiActionTypes.CreateBoardFromTemplate;

    public async Task<AiActionAppliedResult> ApplyAsync(
        AiActionLog log, AiActionContext ctx, CancellationToken ct = default)
    {
        var proposal = Parse(log);

        // 1) The board itself. Created with the acting Manager as the author so the activity log names a
        //    real person rather than the system.
        var board = await _boards.CreateBoardAsync(
            ctx.WorkspaceId,
            new CreateBoardRequest(proposal.BoardName, proposal.BoardDescription),
            ctx.ActingUserId,
            ct);

        var warnings = new List<string>();

        // 2) Columns, in the confirmed order. The order IS the workflow, so `Position` follows the array
        //    index; `ColumnService` appends, which yields exactly that for a brand-new board.
        var columnIdByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var createdColumnIds = new List<Guid>();

        foreach (var column in proposal.Columns)
        {
            var created = await _columns.CreateColumnAsync(
                board.Id,
                new CreateColumnRequest(column.Name, column.IsDone),
                ctx.ActingUserId,
                ct);

            createdColumnIds.Add(created.Id);
            columnIdByName[created.Name] = created.Id;
        }

        // 3) Starter tasks. The validator already guaranteed a known column name; the fallback is the
        //    first non-done lane so a task can never be silently dropped at this stage.
        var fallbackColumnId = createdColumnIds.Count > 0 ? createdColumnIds[0] : Guid.Empty;
        var createdTaskIds = new List<Guid>();

        foreach (var task in proposal.Tasks)
        {
            if (!columnIdByName.TryGetValue(task.ColumnName, out var columnId))
            {
                columnId = fallbackColumnId;
            }

            if (columnId == Guid.Empty)
            {
                warnings.Add($"Bỏ qua task '{task.Title}': bảng không có cột nào để đặt vào.");
                continue;
            }

            var created = await _tasks.CreateTaskAsync(
                board.Id,
                new CreateTaskRequest(
                    ColumnId: columnId,
                    Title: task.Title,
                    Description: task.Description,
                    // Only a MATCHED suggestion is assigned: an unmatched name stays a label for the user
                    // to re-pick in the preview, and assigning nobody is better than assigning wrongly.
                    AssigneeId: task.Assignee?.Matched == true ? task.Assignee.UserId : null,
                    // The proposal carries no dates; the team lead sets them on the board.
                    DueDate: null,
                    // Priority travels as text (the DTO's own contract) and the service parses it, so
                    // the same validation path runs as for an HTTP-created task.
                    Priority: task.Priority),
                ctx.ActingUserId,
                ct);

            createdTaskIds.Add(created.Id);
        }

        _logger.LogInformation(
            "CreateBoardFromTemplate applied (log {LogId}, workspace {WorkspaceId}, board {BoardId}, "
            + "{Columns} column(s), {Tasks} task(s), {Warnings} warning(s)).",
            log.Id, ctx.WorkspaceId, board.Id, createdColumnIds.Count, createdTaskIds.Count, warnings.Count);

        // `EntityId` is the workspace (matching `entity_type = 'Workspace'`); the created board lives in
        // `CreatedBoardId`, which is what Undo reads back.
        return new AiActionAppliedResult(
            AiEntityTypes.Workspace,
            ctx.WorkspaceId,
            createdTaskIds,
            [],
            warnings,
            CreatedColumnIds: createdColumnIds,
            CreatedBoardId: board.Id);
    }

    /// <summary>
    /// Undo = <b>soft-delete the board</b> (decision D14). Deliberately not a hard delete: every FK in
    /// this schema is RESTRICT and the whole design is append-only, so "hide it" is the only reversible,
    /// non-destructive option — and it already hides the board's tasks everywhere (the task query filter
    /// follows its board).
    /// </summary>
    public async Task<IReadOnlyList<string>> UndoAsync(
        AiActionLog log, AiActionContext ctx, CancellationToken ct = default)
    {
        var warnings = new List<string>();

        var boardId = AiActionService.ReadGuid(log.AppliedSnapshot, "createdBoardId");
        if (boardId is null)
        {
            warnings.Add("Hành động không ghi lại board nào; không có gì để hoàn tác.");
            return warnings;
        }

        try
        {
            await _boards.DeleteBoardAsync(boardId.Value, ctx.ActingUserId, ct);
        }
        catch (NotFoundException)
        {
            // Already deleted (by hand or by an earlier Undo): report it instead of throwing, so the log
            // still reaches the Undone state the user asked for.
            warnings.Add("Bảng đã bị xoá trước đó; chỉ ghi nhận hoàn tác.");
        }

        _logger.LogInformation(
            "CreateBoardFromTemplate undone (log {LogId}, board {BoardId}).", log.Id, boardId.Value);

        return warnings;
    }

    /// <summary>Parses the confirmed proposal out of <c>after_snapshot</c>, defensively.</summary>
    private static BoardTemplateProposal Parse(AiActionLog log)
    {
        if (string.IsNullOrWhiteSpace(log.AfterSnapshot))
        {
            throw new BadRequestException("Hành động này không có đề xuất bảng để tạo.");
        }

        BoardTemplateProposal? proposal;

        try
        {
            proposal = JsonSerializer.Deserialize<BoardTemplateProposal>(log.AfterSnapshot, Json);
        }
        catch (JsonException ex)
        {
            throw new BadRequestException($"Đề xuất bảng không phải JSON hợp lệ: {ex.Message}");
        }

        if (proposal is null
            || string.IsNullOrWhiteSpace(proposal.BoardName)
            || proposal.Columns.Count == 0
            || proposal.Tasks.Count == 0)
        {
            throw new BadRequestException("Đề xuất bảng thiếu tên bảng, cột hoặc task.");
        }

        return proposal;
    }
}
