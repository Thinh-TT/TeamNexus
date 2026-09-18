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
/// Applier for <see cref="AiActionTypes.CreateSubtasks"/> (Phase 4 §2.4): creates the confirmed
/// sub-tasks through the Board services (so validation + SignalR broadcast come for free), and
/// reverts them on Undo (soft delete + junction/label cleanup).
/// <para>
/// This class is the only place in the Ai module that touches Tasks/Labels/TaskLabels — and even
/// here every business write goes through <see cref="ITaskService"/>/<see cref="ILabelService"/>;
/// direct DbContext access is limited to reading and to cleaning up <c>task_labels</c> rows
/// (there is no Board API for a junction row of an already soft-deleted task).
/// </para>
/// </summary>
public sealed class CreateSubtasksApplier : IAiActionApplier
{
    /// <summary>Default colours for labels created by the action (D14) — cycling palette.</summary>
    private static readonly string[] LabelPalette =
        ["#6366F1", "#10B981", "#F59E0B", "#EF4444", "#8B5CF6"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TeamNexusDbContext _db;
    private readonly ITaskService _tasks;
    private readonly ILabelService _labels;
    private readonly IColumnService _columns;
    private readonly IWorkspaceMemberService _members;
    private readonly ILogger<CreateSubtasksApplier> _logger;

    public CreateSubtasksApplier(
        TeamNexusDbContext db,
        ITaskService tasks,
        ILabelService labels,
        IColumnService columns,
        IWorkspaceMemberService members,
        ILogger<CreateSubtasksApplier> logger)
    {
        _db = db;
        _tasks = tasks;
        _labels = labels;
        _columns = columns;
        _members = members;
        _logger = logger;
    }

    public string ActionType => AiActionTypes.CreateSubtasks;

    public async Task<AiActionAppliedResult> ApplyAsync(
        AiActionLog log, AiActionContext ctx, CancellationToken ct = default)
    {
        var request = ParseRequest(log);

        var columns = await _columns.GetColumnsAsync(ctx.RequireBoardId(), ctx.ActingUserId, ct);
        var targetColumn = ResolveTargetColumn(request, columns);

        var memberIds = (await _members.GetMembersAsync(ctx.WorkspaceId, ctx.ActingUserId, ct))
            .Select(m => m.UserId)
            .ToHashSet();

        var existingLabels = await LoadLabelsAsync(ctx.WorkspaceId, ct);

        var warnings = new List<string>();
        var createdTaskIds = new List<Guid>();
        var createdLabelIds = new List<Guid>();

        foreach (var task in request.Tasks)
        {
            var assigneeId = task.Assignee?.UserId;
            if (assigneeId.HasValue && !memberIds.Contains(assigneeId.Value))
            {
                warnings.Add(
                    $"Assignee '{task.Assignee?.DisplayName ?? assigneeId.Value.ToString()}' is no longer a "
                    + "workspace member; the task was created unassigned.");
                assigneeId = null;
            }

            var created = await _tasks.CreateTaskAsync(
                ctx.RequireBoardId(),
                new CreateTaskRequest(
                    targetColumn.Id, task.Title, task.Description, assigneeId, null, task.Priority),
                ctx.ActingUserId,
                ct);

            createdTaskIds.Add(created.Id);

            foreach (var label in task.Labels ?? [])
            {
                var labelId = await ResolveLabelIdAsync(label, ctx, existingLabels, createdLabelIds, warnings, ct);
                if (labelId is null)
                {
                    continue;
                }

                await _labels.AttachToTaskAsync(
                    created.Id, new AttachLabelRequest(labelId.Value), ctx.ActingUserId, ct);
            }
        }

        _logger.LogInformation(
            "CreateSubtasks applied (log {LogId}, board {BoardId}): {TaskCount} task(s), {LabelCount} new label(s).",
            log.Id, ctx.RequireBoardId(), createdTaskIds.Count, createdLabelIds.Count);

        return new AiActionAppliedResult(
            AiEntityTypes.Board, ctx.RequireBoardId(), createdTaskIds, createdLabelIds, warnings);
    }

    public async Task<IReadOnlyList<string>> UndoAsync(
        AiActionLog log, AiActionContext ctx, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var taskIds = AiActionService.ReadGuidArray(log.AppliedSnapshot, "createdTaskIds");
        var labelIds = AiActionService.ReadGuidArray(log.AppliedSnapshot, "createdLabelIds");

        if (taskIds.Count == 0 && labelIds.Count == 0)
        {
            return warnings;
        }

        if (taskIds.Count > 0)
        {
            // The Tasks query filter hides soft-deleted rows, so ask with IgnoreQueryFilters:
            // tasks already deleted by a human are skipped instead of throwing 404.
            var activeTaskIds = await _db.Tasks
                .IgnoreQueryFilters()
                .Where(t => taskIds.Contains(t.Id) && t.DeletedAt == null)
                .Select(t => t.Id)
                .ToListAsync(ct);

            foreach (var taskId in activeTaskIds)
            {
                // Soft delete + TaskDeleted broadcast, through the Board service.
                await _tasks.DeleteTaskAsync(taskId, ctx.ActingUserId, ct);
            }

            // TaskLabel has its own query filter that hides junction rows of soft-deleted tasks;
            // the rows still exist in the DB (Restrict FK), so remove them explicitly.
            var links = await _db.TaskLabels
                .IgnoreQueryFilters()
                .Where(tl => taskIds.Contains(tl.TaskId))
                .ToListAsync(ct);

            if (links.Count > 0)
            {
                _db.TaskLabels.RemoveRange(links);
                await _db.SaveChangesAsync(ct);
            }
        }

        foreach (var labelId in labelIds)
        {
            var stillReferenced = await _db.TaskLabels
                .IgnoreQueryFilters()
                .AnyAsync(tl => tl.LabelId == labelId, ct);

            if (stillReferenced)
            {
                warnings.Add($"Label {labelId} is still used by other tasks and was kept.");
                continue;
            }

            try
            {
                await _labels.DeleteLabelAsync(ctx.WorkspaceId, labelId, ctx.ActingUserId, ct);
            }
            catch (NotFoundException)
            {
                warnings.Add($"Label {labelId} no longer exists; nothing to remove.");
            }
        }

        _logger.LogInformation(
            "CreateSubtasks undone (log {LogId}, board {BoardId}): {TaskCount} task(s) soft-deleted, "
            + "{LabelCount} label(s) evaluated, {WarningCount} warning(s).",
            log.Id, ctx.RequireBoardId(), taskIds.Count, labelIds.Count, warnings.Count);

        return warnings;
    }

    // ---- helpers -----------------------------------------------------------

    private static ConfirmSmartSetupRequest ParseRequest(AiActionLog log)
    {
        if (string.IsNullOrWhiteSpace(log.AfterSnapshot))
        {
            throw new BadRequestException("The AI action has no after_snapshot to apply.");
        }

        try
        {
            return JsonSerializer.Deserialize<ConfirmSmartSetupRequest>(log.AfterSnapshot, Json)
                   ?? throw new BadRequestException("The AI action after_snapshot is empty.");
        }
        catch (JsonException ex)
        {
            throw new BadRequestException($"The AI action after_snapshot is not valid JSON: {ex.Message}");
        }
    }

    /// <summary>Target column: the requested one (must belong to the board) or the first by position.</summary>
    private static ColumnResponse ResolveTargetColumn(
        ConfirmSmartSetupRequest request, IReadOnlyList<ColumnResponse> columns)
    {
        if (request.ColumnId is { } columnId)
        {
            return columns.FirstOrDefault(c => c.Id == columnId)
                   ?? throw new BadRequestException("Target column does not belong to this board.");
        }

        return columns.FirstOrDefault()
               ?? throw new BadRequestException("The board has no columns to create the sub-tasks in.");
    }

    /// <summary>Existing workspace labels by name (case-insensitive) → id, refreshed as we create.</summary>
    private async Task<Dictionary<string, Guid>> LoadLabelsAsync(Guid workspaceId, CancellationToken ct)
    {
        var labels = await _db.Labels
            .AsNoTracking()
            .Where(l => l.WorkspaceId == workspaceId)
            .OrderBy(l => l.Name)
            .ToListAsync(ct);

        return labels
            .GroupBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Guid?> ResolveLabelIdAsync(
        SmartSetupLabelSuggestion suggestion,
        AiActionContext ctx,
        Dictionary<string, Guid> existingLabels,
        List<Guid> createdLabelIds,
        List<string> warnings,
        CancellationToken ct)
    {
        var name = suggestion.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        // Reuse a label that already exists (it may have been created after the proposal was shown).
        if (existingLabels.TryGetValue(name, out var existingId))
        {
            return existingId;
        }

        if (suggestion.Exists && suggestion.LabelId is { } suggestedId)
        {
            warnings.Add($"Label '{name}' no longer exists in this workspace; it was skipped.");
            _logger.LogWarning(
                "CreateSubtasks: label {LabelId} ('{Name}') disappeared before apply; skipped.", suggestedId, name);
            return null;
        }

        try
        {
            var created = await _labels.CreateLabelAsync(
                ctx.WorkspaceId,
                new CreateLabelRequest(name, LabelPalette[createdLabelIds.Count % LabelPalette.Length]),
                ctx.ActingUserId,
                ct);

            createdLabelIds.Add(created.Id);
            existingLabels[name] = created.Id;
            return created.Id;
        }
        catch (ConflictException)
        {
            // Created in parallel between our lookup and the insert — reuse the winner.
            var refreshed = await LoadLabelsAsync(ctx.WorkspaceId, ct);
            if (refreshed.TryGetValue(name, out var winnerId))
            {
                existingLabels[name] = winnerId;
                return winnerId;
            }

            throw;
        }
    }
}
