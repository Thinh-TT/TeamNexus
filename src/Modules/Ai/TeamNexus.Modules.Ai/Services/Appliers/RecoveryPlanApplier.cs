using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Appliers;

/// <summary>
/// Applier for <see cref="AiActionTypes.RecoveryPlan"/> (Giai đoạn 14.7 §A5 Phần B): applies the Observer's
/// proactive recovery proposal once a manager approves it, and reverts it exactly on Undo.
/// <para>
/// The four kinds are handled differently on purpose:
/// <list type="bullet">
///   <item><c>ReassignTask</c> / <c>ExtendDeadline</c> — one field each, with the previous value captured so
///   Undo restores it precisely.</item>
///   <item><c>SplitSubtasks</c> — <b>delegated</b> to <see cref="CreateSubtasksApplier"/>. Re-implementing
///   "create a batch of sub-tasks with labels and assignee resolution" here would be a second copy of logic
///   that already handles label reuse, member validation and Undo.</item>
///   <item><c>EscalateManager</c> — writes <b>nothing</b> and returns a warning. This is the one kind whose
///   "success" is the absence of a change, which is exactly why it has a dedicated test: without one it would
///   be an approve-button that silently does nothing.</item>
/// </list>
/// </para>
/// <para>
/// <b>⛔ No transaction is opened here.</b> <see cref="AiActionService.ApproveAsync"/> already opened one, and
/// nesting a second is the Phase 14.5 <b>P2</b> bug (a separate transaction inside an ambient one made
/// approving a move answer 500). Every change below therefore lands in the caller's single
/// <c>SaveChangesAsync</c>.
/// </para>
/// </summary>
public sealed class RecoveryPlanApplier : IAiActionApplier
{
    /// <summary>Matches <c>tasks.due_date</c>'s practical ceiling: a plan must not push a date 100 years out.</summary>
    public const int MaxExtensionDays = 365;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly TeamNexusDbContext _db;
    private readonly ITaskService _tasks;
    private readonly CreateSubtasksApplier _subtasks;
    private readonly ILogger<RecoveryPlanApplier> _logger;

    public RecoveryPlanApplier(
        TeamNexusDbContext db,
        ITaskService tasks,
        CreateSubtasksApplier subtasks,
        ILogger<RecoveryPlanApplier> logger)
    {
        _db = db;
        _tasks = tasks;
        _subtasks = subtasks;
        _logger = logger;
    }

    public string ActionType => AiActionTypes.RecoveryPlan;

    public async Task<AiActionAppliedResult> ApplyAsync(
        AiActionLog log, AiActionContext ctx, CancellationToken ct = default)
    {
        var plan = ParsePlan(log);
        var kind = RecoveryPlanKinds.Canonical(plan.Kind)
                   ?? throw new BadRequestException($"Kịch bản phục hồi không hợp lệ: '{plan.Kind}'.");

        switch (kind)
        {
            case RecoveryPlanKinds.EscalateManager:
                // Nothing to write, and that is the correct behaviour: the whole point of this kind is that no
                // automatic action is appropriate. The warning is what tells the manager the click did its job.
                _logger.LogInformation(
                    "RecoveryPlan {LogId} applied as EscalateManager: no data changed by design.", log.Id);

                return new AiActionAppliedResult(
                    AiEntityTypes.Workspace,
                    ctx.WorkspaceId,
                    [],
                    [],
                    ["Cần họp khẩn — không có thay đổi dữ liệu nào được áp dụng."],
                    RecoveryKind: kind);

            case RecoveryPlanKinds.SplitSubtasks:
                return await SplitAsync(log, ctx, plan, ct);

            case RecoveryPlanKinds.ReassignTask:
                return await ReassignAsync(log, ctx, plan, ct);

            default:
                return await ExtendAsync(log, ctx, plan, ct);
        }
    }

    public async Task<IReadOnlyList<string>> UndoAsync(
        AiActionLog log, AiActionContext ctx, CancellationToken ct = default)
    {
        var kind = AiActionService.ReadString(log.AppliedSnapshot, "recoveryKind");

        if (string.Equals(kind, RecoveryPlanKinds.EscalateManager, StringComparison.OrdinalIgnoreCase))
        {
            // Symmetric with Apply: there was never a change, so there is nothing to revert and the undo
            // succeeds. Failing here would leave an un-undoable approved action in the history forever.
            return [];
        }

        if (string.Equals(kind, RecoveryPlanKinds.SplitSubtasks, StringComparison.OrdinalIgnoreCase))
        {
            // The board the split actually wrote into, recorded in the plan so Undo targets the same one.
            //
            // The narrowing matters: `AiActionContext.BoardId` comes from the LOG's entity id, and this action
            // is workspace-scoped, so without it `CreateSubtasksApplier.UndoAsync` would throw from
            // `RequireBoardId()` and leave an approved action permanently un-undoable.
            var splitBoardId = AiActionService.ReadGuid(log.AfterSnapshot, "boardId")
                               ?? await ResolveFallbackBoardIdAsync(ctx.WorkspaceId, ct);

            return await _subtasks.UndoAsync(log, ctx with { BoardId = splitBoardId }, ct);
        }

        var taskId = AiActionService.ReadGuid(log.AppliedSnapshot, "targetTaskId");
        if (taskId is null)
        {
            return ["Không xác định được thẻ cần hoàn tác."];
        }

        var task = await _db.Tasks
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == taskId.Value && t.DeletedAt == null, ct);

        if (task is null)
        {
            return ["Thẻ đã bị xoá; không còn gì để hoàn tác."];
        }

        var warnings = new List<string>();

        if (string.Equals(kind, RecoveryPlanKinds.ReassignTask, StringComparison.OrdinalIgnoreCase))
        {
            var previous = AiActionService.ReadGuid(log.AppliedSnapshot, "previousAssigneeId");

            // A manager may have reassigned the card by hand since the plan was applied; silently reverting
            // that would undo a human's decision. The card is only restored when it still points at the
            // assignee this plan set.
            var applied = AiActionService.ReadGuid(log.AppliedSnapshot, "newAssigneeId");
            if (applied is not null && task.AssigneeId != applied)
            {
                return ["Người phụ trách đã được đổi thủ công sau đó; không hoàn tác để tránh ghi đè."];
            }

            task.AssigneeId = previous;
        }
        else
        {
            var hadDueDate = AiActionService.ReadBool(log.AppliedSnapshot, "previousDueDateHadValue");
            if (!hadDueDate)
            {
                task.DueDate = null;
            }
            else
            {
                var previous = AiActionService.ReadDateTimeOffset(log.AppliedSnapshot, "previousDueDate");
                if (previous is null)
                {
                    warnings.Add("Không đọc được hạn cũ; giữ nguyên hạn hiện tại.");
                }
                else
                {
                    task.DueDate = previous;
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("RecoveryPlan {LogId} undone ({Kind}).", log.Id, kind);

        return warnings;
    }

    // ---- kinds --------------------------------------------------------------------------------

    /// <summary>
    /// Delegates to the Phase 4 applier with the recovery summary as the action's basis text. The proposal
    /// carries no user-written description, and the validated summary is the honest stand-in.
    /// </summary>
    private async Task<AiActionAppliedResult> SplitAsync(
        AiActionLog log, AiActionContext ctx, RecoveryPlanPayload plan, CancellationToken ct)
    {
        if (plan.Tasks is null || plan.Tasks.Count == 0)
        {
            throw new BadRequestException("Kịch bản phân rã không có thẻ con nào để tạo.");
        }

        // The board the signal came from; the caller recorded it in the plan. Falling back to the workspace's
        // first board keeps a plan applicable when the signal was workspace-wide (e.g. an Overload signal).
        var boardId = plan.BoardId ?? await ResolveFallbackBoardIdAsync(ctx.WorkspaceId, ct);

        var delegated = new AiActionLog
        {
            Id = log.Id,
            Action = AiActionTypes.CreateSubtasks,
            EntityType = AiEntityTypes.Board,
            EntityId = boardId,
            // `CreateSubtasksApplier` reads after_snapshot as a ConfirmSmartSetupRequest, so the payload is
            // re-shaped into that contract rather than passed through.
            AfterSnapshot = JsonSerializer.Serialize(
                new ConfirmSmartSetupRequest(plan.Summary ?? "Kịch bản phục hồi", plan.Summary, plan.Tasks, null),
                Json),
            Status = log.Status,
            RequestedByUserId = log.RequestedByUserId,
        };

        var context = ctx with { BoardId = boardId };

        var result = await _subtasks.ApplyAsync(delegated, context, ct);

        // ⚠️ The delegated applier's writes MUST be carried up into this action's own `applied_snapshot`.
        // `AiActionService` persists only the OUTER result, so dropping these ids meant `UndoAsync` below
        // re-entered `CreateSubtasksApplier` with an empty `createdTaskIds` and soft-deleted nothing — and
        // worse, the outer undo path had no way to discover the omission. Undo is verified end-to-end for
        // exactly this reason.
        //
        // The outer action is workspace-scoped, so its own scope stays that way even though the write
        // happened through the board-scoped delegate.
        return result with
        {
            EntityType = AiEntityTypes.Workspace,
            EntityId = ctx.WorkspaceId,
            RecoveryKind = RecoveryPlanKinds.SplitSubtasks,
            TargetTaskId = plan.TaskId,
        };
    }

    private async Task<AiActionAppliedResult> ReassignAsync(
        AiActionLog log, AiActionContext ctx, RecoveryPlanPayload plan, CancellationToken ct)
    {
        if (plan.TaskId is not { } taskId)
        {
            throw new BadRequestException("Kịch bản điều phối lại thiếu thẻ đích.");
        }

        var task = await LoadActiveTaskAsync(taskId, ct);
        var previousAssigneeId = task.AssigneeId;

        await _tasks.UpdateTaskAsync(
            taskId,
            new UpdateTaskRequest(
                task.Title,
                task.Description,
                // A null new assignee means "unassign", which is a legitimate recovery action (the card is
                // stuck on someone who left). `UpdateTaskAsync` validates membership for a non-null id.
                plan.AssigneeId,
                task.DueDate,
                task.Priority?.ToString()),
            ctx.ActingUserId,
            ct);

        _logger.LogInformation(
            "RecoveryPlan {LogId} applied as ReassignTask on task {TaskId}.", log.Id, taskId);

        return new AiActionAppliedResult(
            AiEntityTypes.Workspace, ctx.WorkspaceId, [], [], [],
            RecoveryKind: RecoveryPlanKinds.ReassignTask,
            TargetTaskId: taskId,
            PreviousAssigneeId: previousAssigneeId,
            NewAssigneeId: plan.AssigneeId);
    }

    private async Task<AiActionAppliedResult> ExtendAsync(
        AiActionLog log, AiActionContext ctx, RecoveryPlanPayload plan, CancellationToken ct)
    {
        if (plan.TaskId is not { } taskId)
        {
            throw new BadRequestException("Kịch bản gia hạn thiếu thẻ đích.");
        }

        var task = await LoadActiveTaskAsync(taskId, ct);
        var previousDueDate = task.DueDate;

        // Bounded relative to NOW, not to the old due date: extending from a date that is already months in
        // the past would produce a "new" deadline that is still overdue.
        var newDueDate = DateTimeOffset.UtcNow.AddDays(Math.Clamp(plan.ExtensionDays ?? 0, 1, MaxExtensionDays));

        await _tasks.UpdateTaskAsync(
            taskId,
            new UpdateTaskRequest(
                task.Title,
                task.Description,
                task.AssigneeId,
                newDueDate,
                task.Priority?.ToString()),
            ctx.ActingUserId,
            ct);

        _logger.LogInformation(
            "RecoveryPlan {LogId} applied as ExtendDeadline on task {TaskId} (+{Days} day(s)).",
            log.Id, taskId, plan.ExtensionDays);

        return new AiActionAppliedResult(
            AiEntityTypes.Workspace, ctx.WorkspaceId, [], [], [],
            RecoveryKind: RecoveryPlanKinds.ExtendDeadline,
            TargetTaskId: taskId,
            PreviousDueDateHadValue: previousDueDate.HasValue,
            PreviousDueDate: previousDueDate);
    }

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>
    /// Loads a task that still exists. <c>IgnoreQueryFilters</c> + an explicit <c>DeletedAt</c> check keeps a
    /// soft-deleted card a clear 404 instead of letting the filter hide it and produce a confusing message.
    /// </summary>
    private async Task<BoardTask> LoadActiveTaskAsync(Guid taskId, CancellationToken ct)
        => await _db.Tasks
               .FirstOrDefaultAsync(t => t.Id == taskId, ct)
           ?? throw new NotFoundException("Thẻ trong kịch bản không còn tồn tại.");

    /// <summary>The workspace's lowest-position board — a deterministic fallback for a workspace-wide signal.</summary>
    private async Task<Guid> ResolveFallbackBoardIdAsync(Guid workspaceId, CancellationToken ct)
        => await _db.Boards
               .AsNoTracking()
               .Where(b => b.WorkspaceId == workspaceId)
               .OrderBy(b => b.CreatedAt)
               .Select(b => (Guid?)b.Id)
               .FirstOrDefaultAsync(ct)
           ?? throw new BadRequestException("Workspace chưa có bảng nào để tạo thẻ con.");

    /// <summary>
    /// Parses the stored plan. A malformed payload is a 400 rather than a 500: the proposal was written by
    /// our own code, so an unreadable one is a client-visible contract violation, not a server fault.
    /// <para>
    /// Public for the same reason the other pure helpers are: the payload contract is the interesting part and
    /// a test must be able to pin it without a database.
    /// </para>
    /// </summary>
    public static RecoveryPlanPayload ParsePlan(AiActionLog log)
    {
        if (string.IsNullOrWhiteSpace(log.AfterSnapshot))
        {
            throw new BadRequestException("Kịch bản phục hồi không có nội dung.");
        }

        try
        {
            return JsonSerializer.Deserialize<RecoveryPlanPayload>(log.AfterSnapshot, Json)
                   ?? throw new BadRequestException("Kịch bản phục hồi rỗng.");
        }
        catch (JsonException ex)
        {
            throw new BadRequestException($"Kịch bản phục hồi không phải JSON hợp lệ: {ex.Message}");
        }
    }
}

/// <summary>
/// The stored shape of a recovery plan (<c>ai_action_logs.after_snapshot</c>), Giai đoạn 14.7 §A5 Phần B.
/// <para>
/// Every field except <see cref="Kind"/> and <see cref="Summary"/> is optional because the four kinds need
/// different inputs; a single record keeps one jsonb shape instead of four, and the applier validates only
/// the fields the chosen <see cref="Kind"/> actually uses.
/// </para>
/// </summary>
public sealed record RecoveryPlanPayload(
    string Kind,
    string? Summary,
    Guid? TaskId = null,
    Guid? AssigneeId = null,
    int? ExtensionDays = null,
    Guid? BoardId = null,
    IReadOnlyList<SmartSetupTaskProposal>? Tasks = null);
