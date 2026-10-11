using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Appliers;

/// <summary>
/// Applier for <see cref="AiActionTypes.UpdateTaskFields"/> (Phase 14.5 §B): the board chat may propose
/// changing an attached task's fields, and this is what writes them once a Manager approves.
/// <para>
/// <b>Every write goes through <see cref="ITaskService"/>.</b> That is not ceremony: the Board service is
/// what validates the title, refuses an assignee who is not a workspace member, normalizes dates to UTC,
/// writes the <c>TaskUpdated</c> activity entry, notifies the new assignee and broadcasts the SignalR
/// event. Writing the row directly here would produce an update that looks fine in the database and is
/// invisible to every other client.
/// </para>
/// <para>
/// <b>Undo restores the whole field set from <c>before_snapshot</c>.</b> <c>UpdateTaskRequest</c> is a
/// full replacement (a null field means "clear"), so the applier captures all five fields before applying
/// and writes them back on Undo — rather than trying to invert the proposal, which would leave a cleared
/// due date unclearable.
/// </para>
/// </summary>
public sealed class UpdateTaskFieldsApplier : IAiActionApplier
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TeamNexusDbContext _db;
    private readonly ITaskService _tasks;
    private readonly ILogger<UpdateTaskFieldsApplier> _logger;

    public UpdateTaskFieldsApplier(
        TeamNexusDbContext db,
        ITaskService tasks,
        ILogger<UpdateTaskFieldsApplier> logger)
    {
        _db = db;
        _tasks = tasks;
        _logger = logger;
    }

    public string ActionType => AiActionTypes.UpdateTaskFields;

    public async Task<AiActionAppliedResult> ApplyAsync(
        AiActionLog log, AiActionContext ctx, CancellationToken ct = default)
    {
        var boardId = ctx.RequireBoardId();
        var proposal = ParseProposal(log);

        var taskId = proposal.TaskId
            ?? throw new BadRequestException("Đề xuất cập nhật thiếu thẻ cần thao tác (taskId).");

        // The task must be on the board the action is scoped to. The validator already checked this at
        // submit time, but the task could have been moved or the request hand-crafted — and an applier is
        // the last place where a wrong target can still be refused.
        var current = await _tasks.GetTaskByIdAsync(taskId, ctx.ActingUserId, ct);

        if (current.BoardId != boardId)
        {
            throw new BadRequestException("Thẻ cần cập nhật không thuộc bảng này.");
        }

        // Capture the WHOLE prior field set here, not at request time: `UpdateTaskRequest` is a full
        // replacement, so Undo has to restore five fields, and the only moment where they are known to be
        // the values that were really overwritten is just before the write. Written to the log inside the
        // caller's transaction (`ApproveAsync`), so the snapshot and the change commit together.
        await CaptureBeforeSnapshotAsync(log, current, ct);

        // A proposed priority is already canonical (the validator rejected anything unknown), but it is
        // re-read from the snapshot rather than trusted as an enum: that keeps the applier's contract
        // "text in, text out" and matches what ValidateConfirmRequest does for sub-tasks.
        var request = new UpdateTaskRequest(
            proposal.Title ?? current.Title,
            proposal.Description ?? current.Description,
            proposal.AssigneeId ?? current.AssigneeId,
            proposal.DueDate ?? current.DueDate,
            proposal.Priority ?? current.Priority);

        var updated = await _tasks.UpdateTaskAsync(taskId, request, ctx.ActingUserId, ct);

        _logger.LogInformation(
            "UpdateTaskFields applied (log {LogId}, task {TaskId}, board {BoardId}) by user {UserId}.",
            log.Id, taskId, boardId, ctx.ActingUserId);

        return new AiActionAppliedResult(
            AiEntityTypes.Task,
            taskId,
            CreatedTaskIds: [],
            CreatedLabelIds: [],
            Warnings: [],
            UpdatedTaskId: taskId);
    }

    public async Task<IReadOnlyList<string>> UndoAsync(
        AiActionLog log, AiActionContext ctx, CancellationToken ct = default)
    {
        var warnings = new List<string>();

        var taskId = AiActionService.ReadGuid(log.AppliedSnapshot, "updatedTaskId")
                     ?? ParseProposal(log).TaskId;

        if (taskId is null)
        {
            warnings.Add("Không xác định được thẻ đã cập nhật nên không thể hoàn tác.");
            return warnings;
        }

        var before = ParseBeforeSnapshot(log);

        if (before is null)
        {
            warnings.Add("Không có ảnh chụp trước khi cập nhật nên không thể hoàn tác chính xác.");
            return warnings;
        }

        // The Tasks query filter hides soft-deleted rows, so a task deleted by a human since the approval
        // throws NotFound — reported as a warning instead of failing the whole Undo.
        try
        {
            await _tasks.UpdateTaskAsync(
                taskId.Value,
                new UpdateTaskRequest(
                    before.Title,
                    before.Description,
                    before.AssigneeId,
                    before.DueDate,
                    before.Priority),
                ctx.ActingUserId,
                ct);
        }
        catch (NotFoundException)
        {
            warnings.Add("Thẻ đã bị xoá nên không thể hoàn tác cập nhật.");
            return warnings;
        }

        _logger.LogInformation(
            "UpdateTaskFields undone (log {LogId}, task {TaskId}).", log.Id, taskId.Value);

        return warnings;
    }

    // ---- snapshot shapes ---------------------------------------------------

    /// <summary>
    /// Stores the task's current five fields in <c>before_snapshot</c> so Undo can restore them.
    /// <para>
    /// Written through EF on the tracked row and saved immediately (inside the caller's transaction), not
    /// assigned to the detached <paramref name="log"/> instance: the applier receives the entity the
    /// service loaded <c>AsNoTracking</c>, so mutating it would be silently discarded.
    /// </para>
    /// </summary>
    private async Task CaptureBeforeSnapshotAsync(
        AiActionLog log, TaskResponse current, CancellationToken ct)
    {
        var snapshot = JsonSerializer.Serialize(
            new UpdateTaskRequest(
                current.Title,
                current.Description,
                current.AssigneeId,
                current.DueDate,
                current.Priority),
            Json);

        // Overwrites any producer-supplied value on purpose: for this action the only correct
        // "before" is the state observed at apply time.
        await _db.AiActionLogs
            .Where(x => x.Id == log.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.BeforeSnapshot, snapshot),
                ct);
    }

    /// <summary>
    /// The <c>after_snapshot</c> as this applier reads it. Deliberately a private shape rather than the
    /// board-chat DTO: the applier is a Phase 4 contract that must keep working for every producer, and
    /// binding it to a Phase 14.5 type would make the chat's DTO part of the Accountability Layer's API.
    /// </summary>
    private sealed record ProposalSnapshot(
        Guid? TaskId,
        string? Title,
        string? Description,
        string? Priority,
        DateTimeOffset? DueDate,
        Guid? AssigneeId);

    private static ProposalSnapshot ParseProposal(AiActionLog log)
    {
        if (string.IsNullOrWhiteSpace(log.AfterSnapshot))
        {
            throw new BadRequestException("Đề xuất cập nhật thẻ không có nội dung.");
        }

        try
        {
            return JsonSerializer.Deserialize<ProposalSnapshot>(log.AfterSnapshot, Json)
                   ?? throw new BadRequestException("Đề xuất cập nhật thẻ rỗng.");
        }
        catch (JsonException ex)
        {
            throw new BadRequestException($"Đề xuất cập nhật thẻ không phải JSON hợp lệ: {ex.Message}");
        }
    }

    /// <summary>The full previous field set, captured at apply time for a faithful Undo.</summary>
    private static UpdateTaskRequest? ParseBeforeSnapshot(AiActionLog log)
    {
        if (string.IsNullOrWhiteSpace(log.BeforeSnapshot))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<UpdateTaskRequest>(log.BeforeSnapshot, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
