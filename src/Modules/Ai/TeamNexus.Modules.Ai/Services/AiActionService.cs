using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;
using BoardEntity = TeamNexus.Persistence.Data.Entities.Board;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// The single gateway every AI write action must pass through (Phase 4 Â§2, tech docs Â§2.4):
/// a Pending log is written first (no real data touched), and only Approve applies it through an
/// <see cref="IAiActionApplier"/>. Reject/Undo are log-only state transitions.
/// </summary>
public interface IAiActionService
{
    /// <summary>
    /// Records a Smart Setup proposal as a <c>Pending</c> action. Requires Manager/Admin (403),
    /// 404 for an unknown board, 400 on invalid input. Never writes tasks/labels.
    /// </summary>
    Task<AiActionLogResponse> RequestCreateSubtasksAsync(
        Guid boardId, ConfirmSmartSetupRequest request, Guid userId, CancellationToken ct = default);

    /// <summary>Board-scoped action history, newest first, optionally filtered by status (Manager/Admin).</summary>
    Task<IReadOnlyList<AiActionLogResponse>> ListAsync(
        Guid boardId, AiActionStatus? status, int take, Guid userId, CancellationToken ct = default);

    /// <summary>One action with its snapshots (Manager/Admin of the owning workspace).</summary>
    Task<AiActionLogDetailResponse> GetAsync(Guid logId, Guid userId, CancellationToken ct = default);

    /// <summary>Applies a Pending action; 409 when it is no longer Pending (double decision).</summary>
    Task<AiActionLogDetailResponse> ApproveAsync(Guid logId, Guid userId, CancellationToken ct = default);

    /// <summary>Rejects a Pending action (optional note); 409 when it is no longer Pending.</summary>
    Task<AiActionLogDetailResponse> RejectAsync(Guid logId, string? note, Guid userId, CancellationToken ct = default);

    /// <summary>Reverts an Approved action; 409 unless the action is Approved.</summary>
    Task<AiActionLogDetailResponse> UndoAsync(Guid logId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Internal API for the AI Agent (Phase 7 Â§4.6, S4): records an agent output as a <c>Pending</c>
    /// <c>PostComment</c>/<c>PostAttachment</c> action.
    /// <para>
    /// <b>Not exposed on any HTTP route.</b> The trust boundary is this in-process call, so instead of
    /// <c>RequireManagerAsync</c> (which would 403 the agent, who is only a Member) it asserts that
    /// the task is currently assigned to that agent and that the caller really is the workspace's
    /// <c>member_type = 'ai_agent'</c> row.
    /// </para>
    /// </summary>
    Task<AiActionLogResponse> RequestAgentOutputAsync(
        Guid taskId,
        Guid agentUserId,
        string actionType,
        string afterSnapshotJson,
        string basisJson,
        CancellationToken ct = default);

    /// <summary>
    /// Records a <b>human-initiated</b> comment on a task as a <c>Pending</c> <c>PostComment</c> action
    /// (Phase 14 §2.2). The AI Task Chat's "lưu thành bình luận" uses this: the answer is prose the user
    /// read and chose to keep, and the author of the resulting comment is that user.
    /// <para>
    /// <b>Why not <see cref="RequestAgentOutputAsync"/>:</b> that one is the AI executor's internal
    /// channel and asserts the task is assigned to the workspace's agent. A chat user is a different
    /// actor entirely, so reusing it would reject every legitimate save with a 403 that talks about an
    /// AI Agent the user never mentioned. The validations here are the human ones — Member+ and a
    /// visible task — and the resulting log still needs a Manager to approve it, which is what keeps the
    /// Accountability Layer's "no business write without a decision" invariant intact.
    /// </para>
    /// </summary>
    Task<AiActionLogResponse> RequestChatCommentAsync(
        Guid taskId,
        Guid userId,
        string afterSnapshotJson,
        string basisJson,
        CancellationToken ct = default);

    /// <summary>
    /// Records a reviewed AI Board Template proposal as a <c>Pending</c>
    /// <c>CreateBoardFromTemplate</c> action scoped to the workspace (Phase 14 §4, decision D12).
    /// Requires Manager/Admin (403), 404 for an unknown workspace. Never writes a board — only the log.
    /// </summary>
    Task<AiActionLogResponse> RequestCreateBoardFromTemplateAsync(
        Guid workspaceId,
        Guid userId,
        string afterSnapshotJson,
        string basisJson,
        CancellationToken ct = default);
}

public sealed class AiActionService : IAiActionService
{
    /// <summary>Length of the description excerpt kept inside <c>basis</c> (token/cost control).</summary>
    public const int DescriptionExcerptLength = 300;

    public const int MaxSummaryLength = 2000;

    public const int MaxDecisionNoteLength = 500;

    public const int DefaultListTake = 20;

    public const int MaxListTake = 100;

    /// <summary>camelCase JSON; also used for the jsonb snapshots (Phase 3 Â§4.1 pattern).</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        // Phase 14 (§6, P3): the board template added two OPTIONAL snapshot fields
        // (createdBoardId/createdColumnIds). Skipping nulls keeps every pre-Phase-14 snapshot at its
        // exact previous shape instead of growing two `null` keys, and keeps "absent" == "null", which
        // is how the older appliers read them.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IWorkspaceMemberService _members;
    private readonly IColumnService _columns;
    private readonly IReadOnlyList<IAiActionApplier> _appliers;
    private readonly DeepSeekOptions _options;
    private readonly ILogger<AiActionService> _logger;

    public AiActionService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IWorkspaceMemberService members,
        IColumnService columns,
        IEnumerable<IAiActionApplier> appliers,
        IOptions<DeepSeekOptions> options,
        ILogger<AiActionService> logger)
    {
        _db = db;
        _access = access;
        _members = members;
        _columns = columns;
        _appliers = appliers.ToList();
        _options = options.Value;
        _logger = logger;
    }

    // ---- request (Pending, no real writes) ---------------------------------

    public async Task<AiActionLogResponse> RequestCreateSubtasksAsync(
        Guid boardId,
        ConfirmSmartSetupRequest request,
        Guid userId,
        CancellationToken ct = default)
    {
        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == boardId, ct)
            ?? throw new NotFoundException("Board not found.");

        await _access.RequireManagerAsync(board.WorkspaceId, userId, ct);

        // Context is loaded read-only â€” the same sources the Smart Setup proposal was built from.
        var columns = await _columns.GetColumnsAsync(boardId, userId, ct);
        var members = await _members.GetMembersAsync(board.WorkspaceId, userId, ct);
        var labels = await LoadWorkspaceLabelsAsync(board.WorkspaceId, ct);

        ValidateConfirmRequest(request, boardId, columns, members, labels, _options.MaxTaskCount);

        var log = new AiActionLog
        {
            Action = AiActionTypes.CreateSubtasks,
            EntityType = AiEntityTypes.Board,
            EntityId = boardId,
            Basis = BuildBasis(board, request, members.Count, columns.Count),
            AfterSnapshot = BuildAfterSnapshotJson(request),
            Status = AiActionStatus.Pending,
            RequestedByUserId = userId,
        };

        // ONLY the log row: approving it (not requesting it) is what writes tasks/labels.
        _db.AiActionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AI action {Action} requested as Pending (log {LogId}, board {BoardId}, {TaskCount} task(s)).",
            log.Action, log.Id, boardId, request.Tasks.Count);

        var names = await LoadUserNamesAsync([userId], ct);
        return BuildResponse(log, names.GetValueOrDefault(userId));
    }

    // ---- request (agent output, internal â€” Phase 7 Â§4.6) --------------------

    public async Task<AiActionLogResponse> RequestAgentOutputAsync(
        Guid taskId,
        Guid agentUserId,
        string actionType,
        string afterSnapshotJson,
        string basisJson,
        CancellationToken ct = default)
    {
        if (!string.Equals(actionType, AiActionTypes.PostComment, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(actionType, AiActionTypes.PostAttachment, StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException($"Unsupported agent output action: {actionType}.");
        }

        if (string.IsNullOrWhiteSpace(afterSnapshotJson))
        {
            throw new BadRequestException("The agent output has no after_snapshot.");
        }

        var task = await _db.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        // S4: the agent is a Member, so RequireManagerAsync would 403 it. These two asserts are the
        // replacement guard â€” "the task really is assigned to this agent" and "this really is the
        // workspace's agent row" â€” and they cannot be reached from HTTP.
        if (task.AssigneeId != agentUserId)
        {
            throw new ForbiddenException("The task is not assigned to this AI Agent.");
        }

        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == task.BoardId, ct)
            ?? throw new NotFoundException("Board not found.");

        var isAgent = await _db.WorkspaceMembers
            .AsNoTracking()
            .AnyAsync(wm => wm.WorkspaceId == board.WorkspaceId
                            && wm.UserId == agentUserId
                            && wm.MemberType == MemberType.AiAgent, ct);

        if (!isAgent)
        {
            throw new ForbiddenException("The acting user is not the AI Agent of this workspace.");
        }

        var log = new AiActionLog
        {
            Action = actionType,
            EntityType = AiEntityTypes.Task,
            EntityId = taskId,
            Basis = string.IsNullOrWhiteSpace(basisJson) ? "{}" : basisJson,
            AfterSnapshot = afterSnapshotJson,
            Status = AiActionStatus.Pending,
            RequestedByUserId = agentUserId,
        };

        // ONLY the log row; approving it is what writes the comment/attachment.
        _db.AiActionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AI action {Action} requested by the AI Agent as Pending (log {LogId}, task {TaskId}).",
            log.Action, log.Id, taskId);

        var names = await LoadUserNamesAsync([agentUserId], ct);
        return BuildResponse(log, names.GetValueOrDefault(agentUserId));
    }

    // ---- request (human-initiated chat comment, Phase 14 §2.2) --------------

    public async Task<AiActionLogResponse> RequestChatCommentAsync(
        Guid taskId,
        Guid userId,
        string afterSnapshotJson,
        string basisJson,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(afterSnapshotJson))
        {
            throw new BadRequestException("Bình luận không có nội dung.");
        }

        // The Tasks query filter makes a soft-deleted task a 404: there is nothing to comment on.
        var task = await _db.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == task.BoardId, ct)
            ?? throw new NotFoundException("Board not found.");

        // Member+ — deliberately NOT the agent assertions of RequestAgentOutputAsync: the person saving
        // a chat answer is an ordinary workspace member, and the approval step (Manager+) is what makes
        // the write accountable, not the request step.
        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);

        var log = new AiActionLog
        {
            Action = AiActionTypes.PostComment,
            EntityType = AiEntityTypes.Task,
            EntityId = taskId,
            Basis = string.IsNullOrWhiteSpace(basisJson) ? "{}" : basisJson,
            AfterSnapshot = afterSnapshotJson,
            Status = AiActionStatus.Pending,
            RequestedByUserId = userId,
        };

        // ONLY the log row. The comment itself is written when a Manager approves it, through the same
        // PostCommentApplier the AI Agent's output uses (author = this user, undo = soft delete).
        _db.AiActionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AI chat comment for task {TaskId} requested as Pending by user {UserId} (log {LogId}).",
            taskId, userId, log.Id);

        var names = await LoadUserNamesAsync([userId], ct);
        return BuildResponse(log, names.GetValueOrDefault(userId));
    }

    // ---- request (board template, Phase 14 §4) -----------------------------

    public async Task<AiActionLogResponse> RequestCreateBoardFromTemplateAsync(
        Guid workspaceId,
        Guid userId,
        string afterSnapshotJson,
        string basisJson,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(afterSnapshotJson))
        {
            throw new BadRequestException("Đề xuất bảng không có nội dung.");
        }

        var workspaceExists = await _db.Workspaces
            .AsNoTracking()
            .AnyAsync(w => w.Id == workspaceId, ct);

        if (!workspaceExists)
        {
            throw new NotFoundException("Workspace not found.");
        }

        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var log = new AiActionLog
        {
            Action = AiActionTypes.CreateBoardFromTemplate,
            EntityType = AiEntityTypes.Workspace,
            EntityId = workspaceId,
            Basis = string.IsNullOrWhiteSpace(basisJson) ? "{}" : basisJson,
            AfterSnapshot = afterSnapshotJson,
            Status = AiActionStatus.Pending,
            RequestedByUserId = userId,
        };

        // ONLY the log row: approving it (not requesting it) is what creates the board, its columns and
        // its starter tasks.
        _db.AiActionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Board template requested as Pending (log {LogId}, workspace {WorkspaceId}, {Tasks} task(s)).",
            log.Id, workspaceId, CountTasks(afterSnapshotJson));

        var names = await LoadUserNamesAsync([userId], ct);
        return BuildResponse(log, names.GetValueOrDefault(userId));
    }

    // ---- read --------------------------------------------------------------
    public async Task<IReadOnlyList<AiActionLogResponse>> ListAsync(
        Guid boardId,
        AiActionStatus? status,
        int take,
        Guid userId,
        CancellationToken ct = default)
    {
        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == boardId, ct)
            ?? throw new NotFoundException("Board not found.");

        await _access.RequireManagerAsync(board.WorkspaceId, userId, ct);

        var clampedTake = Math.Clamp(take <= 0 ? DefaultListTake : take, 1, MaxListTake);

        var query = _db.AiActionLogs
            .AsNoTracking()
            .Where(x => x.EntityType == AiEntityTypes.Board && x.EntityId == boardId);

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        var logs = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(clampedTake)
            .ToListAsync(ct);

        var names = await LoadUserNamesAsync(
            logs.Select(l => l.RequestedByUserId)
                .Concat(logs.Where(l => l.DecidedByUserId.HasValue).Select(l => l.DecidedByUserId!.Value)),
            ct);

        return logs.Select(l => BuildResponse(
            l,
            names.GetValueOrDefault(l.RequestedByUserId),
            l.DecidedByUserId.HasValue ? names.GetValueOrDefault(l.DecidedByUserId.Value) : null)).ToList();
    }

    public async Task<AiActionLogDetailResponse> GetAsync(
        Guid logId, Guid userId, CancellationToken ct = default)
    {
        var log = await LoadLogAsync(logId, ct);
        await ResolveAsync(log, userId, ct);
        return await BuildDetailAsync(logId, ct);
    }

    // ---- decisions ---------------------------------------------------------

    public async Task<AiActionLogDetailResponse> ApproveAsync(
        Guid logId, Guid userId, CancellationToken ct = default)
    {
        var log = await LoadLogAsync(logId, ct);
        var ctx = await ResolveAsync(log, userId, ct);
        var applier = ResolveApplier(log.Action);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        // Compare-and-swap on the status column: without a concurrency token this is what makes
        // "approve twice" impossible â€” the loser gets 0 affected rows and rolls back (409).
        var now = DateTimeOffset.UtcNow;
        var affected = await _db.AiActionLogs
            .Where(x => x.Id == logId && x.Status == AiActionStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, AiActionStatus.Approved)
                .SetProperty(x => x.DecidedByUserId, userId)
                .SetProperty(x => x.DecidedAt, now)
                .SetProperty(x => x.UpdatedAt, now), ct);

        if (affected == 0)
        {
            throw new ConflictException(
                "This AI action is no longer Pending (it was already decided elsewhere).");
        }

        var applied = await applier.ApplyAsync(log, ctx, ct);

        var appliedJson = BuildAppliedSnapshotJson(applied);
        await _db.AiActionLogs
            .Where(x => x.Id == logId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.AppliedSnapshot, appliedJson)
                .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), ct);

        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "AI action {LogId} ({Action}) approved: {TaskCount} task(s), {LabelCount} new label(s), "
            + "{CommentCount} comment(s), {AttachmentCount} attachment(s), {WarningCount} warning(s).",
            logId, log.Action, applied.CreatedTaskIds.Count, applied.CreatedLabelIds.Count,
            applied.CreatedCommentId.HasValue ? 1 : 0, applied.CreatedAttachmentId.HasValue ? 1 : 0,
            applied.Warnings.Count);

        return await BuildDetailAsync(logId, ct);
    }

    public async Task<AiActionLogDetailResponse> RejectAsync(
        Guid logId, string? note, Guid userId, CancellationToken ct = default)
    {
        var log = await LoadLogAsync(logId, ct);
        await ResolveAsync(log, userId, ct);

        var trimmedNote = TrimToNull(note);
        if (trimmedNote is { Length: > MaxDecisionNoteLength })
        {
            trimmedNote = trimmedNote[..MaxDecisionNoteLength];
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var affected = await _db.AiActionLogs
            .Where(x => x.Id == logId && x.Status == AiActionStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, AiActionStatus.Rejected)
                .SetProperty(x => x.DecidedByUserId, userId)
                .SetProperty(x => x.DecidedAt, now)
                .SetProperty(x => x.DecisionNote, trimmedNote)
                .SetProperty(x => x.UpdatedAt, now), ct);

        if (affected == 0)
        {
            throw new ConflictException(
                "This AI action is no longer Pending (it was already decided elsewhere).");
        }

        await transaction.CommitAsync(ct);

        _logger.LogInformation("AI action {LogId} ({Action}) rejected.", logId, log.Action);

        return await BuildDetailAsync(logId, ct);
    }

    public async Task<AiActionLogDetailResponse> UndoAsync(
        Guid logId, Guid userId, CancellationToken ct = default)
    {
        var log = await LoadLogAsync(logId, ct);
        var ctx = await ResolveAsync(log, userId, ct);
        var applier = ResolveApplier(log.Action);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var affected = await _db.AiActionLogs
            .Where(x => x.Id == logId && x.Status == AiActionStatus.Approved)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, AiActionStatus.Undone)
                .SetProperty(x => x.DecidedByUserId, userId)
                .SetProperty(x => x.DecidedAt, now)
                .SetProperty(x => x.UpdatedAt, now), ct);

        if (affected == 0)
        {
            throw new ConflictException("Only an Approved AI action can be undone.");
        }

        var warnings = await applier.UndoAsync(log, ctx, ct);

        if (warnings.Count > 0)
        {
            var merged = MergeUndoWarnings(log.AppliedSnapshot, warnings);
            await _db.AiActionLogs
                .Where(x => x.Id == logId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.AppliedSnapshot, merged)
                    .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), ct);
        }

        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "AI action {LogId} ({Action}) undone ({WarningCount} warning(s)).",
            logId, log.Action, warnings.Count);

        return await BuildDetailAsync(logId, ct);
    }

    // ---- guards ------------------------------------------------------------

    private async Task<AiActionLog> LoadLogAsync(Guid logId, CancellationToken ct)
        => await _db.AiActionLogs
               .AsNoTracking()
               .FirstOrDefaultAsync(x => x.Id == logId, ct)
           ?? throw new NotFoundException("AI action not found.");

    /// <summary>
    /// Validates the log scope and requires Manager/Admin in the owning workspace.
    /// <para>
    /// Phase 7 Â§4.6 adds the <c>Task</c> branch (<c>entity_type = 'Task'</c>, <c>entity_id</c> =
    /// taskId). The <c>Board</c> branch is untouched â€” Phase 4 behaviour is what group I of the
    /// verification run re-proves.
    /// </para>
    /// </summary>
    private async Task<AiActionContext> ResolveAsync(AiActionLog log, Guid userId, CancellationToken ct)
    {
        if (string.Equals(log.EntityType, AiEntityTypes.Task, StringComparison.OrdinalIgnoreCase))
        {
            if (log.EntityId is null)
            {
                throw new BadRequestException($"AI action {log.Id} is not scoped to a task.");
            }

            // The Tasks query filter makes a soft-deleted task a 404: its agent output has no target.
            var task = await _db.Tasks
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == log.EntityId.Value, ct)
                ?? throw new NotFoundException("Task not found.");

            var taskBoard = await _db.Boards
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == task.BoardId, ct)
                ?? throw new NotFoundException("Board not found.");

            await _access.RequireManagerAsync(taskBoard.WorkspaceId, userId, ct);

            return new AiActionContext(taskBoard.Id, taskBoard.WorkspaceId, userId, task.Id);
        }

        if (string.Equals(log.EntityType, AiEntityTypes.Workspace, StringComparison.OrdinalIgnoreCase))
        {
            if (log.EntityId is null)
            {
                throw new BadRequestException($"AI action {log.Id} is not scoped to a workspace.");
            }

            var scopedWorkspaceId = log.EntityId.Value;

            // 404 when the workspace is gone/not visible, 403 for a plain Member — the same order as every
            // other workspace-scoped gate in the codebase.
            var workspaceExists = await _db.Workspaces
                .AsNoTracking()
                .AnyAsync(w => w.Id == scopedWorkspaceId, ct);

            if (!workspaceExists)
            {
                throw new NotFoundException("Workspace not found.");
            }

            await _access.RequireManagerAsync(scopedWorkspaceId, userId, ct);

            // BoardId stays NULL on purpose: this action is the one that CREATES the board
            // (Phase 14 §6, decisions D12/P1/P2).
            return new AiActionContext(null, scopedWorkspaceId, userId);
        }

        if (!string.Equals(log.EntityType, AiEntityTypes.Board, StringComparison.OrdinalIgnoreCase)
            || log.EntityId is null)
        {
            throw new BadRequestException($"AI action {log.Id} is not scoped to a board.");
        }

        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == log.EntityId.Value, ct)
            ?? throw new NotFoundException("Board not found.");

        await _access.RequireManagerAsync(board.WorkspaceId, userId, ct);

        return new AiActionContext(board.Id, board.WorkspaceId, userId);
    }
    private IAiActionApplier ResolveApplier(string action)
        => _appliers.FirstOrDefault(
               a => string.Equals(a.ActionType, action, StringComparison.OrdinalIgnoreCase))
           ?? throw new BadRequestException($"Unsupported AI action: {action}.");

    private async Task<AiActionLogDetailResponse> BuildDetailAsync(Guid logId, CancellationToken ct)
    {
        var log = await LoadLogAsync(logId, ct);
        var names = await LoadUserNamesAsync(
            log.DecidedByUserId.HasValue
                ? [log.RequestedByUserId, log.DecidedByUserId.Value]
                : [log.RequestedByUserId],
            ct);

        return BuildDetailResponse(
            log,
            names.GetValueOrDefault(log.RequestedByUserId),
            log.DecidedByUserId.HasValue ? names.GetValueOrDefault(log.DecidedByUserId.Value) : null);
    }

    private Task<List<WorkspaceLabel>> LoadWorkspaceLabelsAsync(Guid workspaceId, CancellationToken ct)
        => _db.Labels
            .Where(l => l.WorkspaceId == workspaceId)
            .OrderBy(l => l.Name)
            .Select(l => new WorkspaceLabel(l.Id, l.Name))
            .AsNoTracking()
            .ToListAsync(ct);

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

    // ---- pure validation / JSON helpers (verify-friendly, no DB) -----------

    /// <summary>
    /// Server-side validation of a confirmed proposal (Phase 4 Â§0 â€” D12). Throws
    /// <see cref="BadRequestException"/> (400) on the first violation; nothing is written when it throws.
    /// </summary>
    public static void ValidateConfirmRequest(
        ConfirmSmartSetupRequest request,
        Guid boardId,
        IReadOnlyList<ColumnResponse> columns,
        IReadOnlyList<WorkspaceMemberResponse> members,
        IReadOnlyList<WorkspaceLabel> labels,
        int maxTaskCount)
    {
        var description = TrimToNull(request.Description);
        if (description is null || description.Length > SmartSetupService.MaxDescriptionLength)
        {
            throw new BadRequestException(
                $"Description must be 1â€“{SmartSetupService.MaxDescriptionLength} characters.");
        }

        var tasks = request.Tasks;
        if (tasks is null || tasks.Count == 0)
        {
            throw new BadRequestException("At least one sub-task is required.");
        }

        if (maxTaskCount > 0 && tasks.Count > maxTaskCount)
        {
            throw new BadRequestException(
                $"A maximum of {maxTaskCount} sub-tasks can be applied in one AI action (received {tasks.Count}).");
        }

        if (request.ColumnId is { } columnId && columns.All(c => c.Id != columnId))
        {
            throw new BadRequestException("Target column does not belong to this board.");
        }

        var memberIds = members.Select(m => m.UserId).ToHashSet();
        var labelIds = labels.Select(l => l.Id).ToHashSet();

        foreach (var task in tasks)
        {
            if (task is null)
            {
                throw new BadRequestException("Sub-task entries must not be null.");
            }

            var title = TrimToNull(task.Title);
            if (title is null || title.Length > SmartSetupService.MaxTitleLength)
            {
                throw new BadRequestException(
                    $"Sub-task title must be 1â€“{SmartSetupService.MaxTitleLength} characters.");
            }

            if (task.Description is { Length: > SmartSetupService.MaxTaskDescriptionLength })
            {
                throw new BadRequestException(
                    $"Sub-task description must be at most {SmartSetupService.MaxTaskDescriptionLength} characters.");
            }

            if (TrimToNull(task.Priority) is { } priority
                && !Enum.TryParse<TaskPriority>(priority, ignoreCase: true, out _))
            {
                throw new BadRequestException("Priority must be one of: Low, Medium, High, Urgent.");
            }

            var taskLabels = task.Labels;
            if (taskLabels is { Count: > SmartSetupService.MaxLabelsPerTask })
            {
                throw new BadRequestException(
                    $"A sub-task can carry at most {SmartSetupService.MaxLabelsPerTask} labels.");
            }

            foreach (var label in taskLabels ?? [])
            {
                var name = TrimToNull(label.Name);
                if (name is null || name.Length > SmartSetupService.MaxLabelLength)
                {
                    throw new BadRequestException(
                        $"Label name must be 1â€“{SmartSetupService.MaxLabelLength} characters.");
                }

                if (label.Exists && label.LabelId.HasValue && !labelIds.Contains(label.LabelId.Value))
                {
                    throw new BadRequestException($"Label '{name}' does not belong to this workspace.");
                }
            }

            if (task.Assignee?.UserId is { } assigneeId && !memberIds.Contains(assigneeId))
            {
                throw new BadRequestException("Assignee is not a member of this workspace.");
            }
        }
    }

    /// <summary>
    /// jsonb <c>basis</c>: a cost-conscious summary of the input (length + excerpt), never the raw
    /// prompt and never any secret.
    /// </summary>
    public static string BuildBasis(
        BoardEntity board, ConfirmSmartSetupRequest request, int memberCount, int columnCount)
    {
        var description = request.Description?.Trim() ?? string.Empty;

        return JsonSerializer.Serialize(new
        {
            boardId = board.Id,
            boardName = board.Name,
            workspaceId = board.WorkspaceId,
            descriptionLength = description.Length,
            descriptionExcerpt = description.Length <= DescriptionExcerptLength
                ? description
                : description[..DescriptionExcerptLength],
            memberCount,
            columnCount,
            taskCount = request.Tasks?.Count ?? 0,
            requestedAt = DateTimeOffset.UtcNow,
        }, Json);
    }

    /// <summary>jsonb <c>after_snapshot</c>: the confirmed proposal, with the summary capped.</summary>
    public static string BuildAfterSnapshotJson(ConfirmSmartSetupRequest request)
        => JsonSerializer.Serialize(
            request with { Summary = CapLength(request.Summary, MaxSummaryLength) }, Json);

    /// <summary>
    /// jsonb <c>applied_snapshot</c>: what the applier actually wrote (Undo's basis). Phase 7 added
    /// the two nullable output ids; <c>createdTaskIds</c>/<c>createdLabelIds</c> keep their names and
    /// meaning so the Phase 4 <c>CreateSubtasks</c> undo path reads exactly what it always did.
    /// </summary>
    public static string BuildAppliedSnapshotJson(AiActionAppliedResult result)
        => JsonSerializer.Serialize(new
        {
            entityType = result.EntityType,
            entityId = result.EntityId,
            createdTaskIds = result.CreatedTaskIds,
            createdLabelIds = result.CreatedLabelIds,
            createdCommentId = result.CreatedCommentId,
            createdAttachmentId = result.CreatedAttachmentId,
            // Phase 14 §6 (P3): the board template creates a BOARD plus its COLUMNS and Undo needs both
            // ids. Nulls are omitted (see the `Json` options), so every pre-Phase-14 snapshot keeps its
            // exact previous key set — the additions are purely additive.
            createdBoardId = result.CreatedBoardId,
            createdColumnIds = result.CreatedColumnIds,
            warnings = result.Warnings,
            appliedAt = DateTimeOffset.UtcNow,
        }, Json);

    /// <summary>
    /// Adds the undo warnings to an existing <c>applied_snapshot</c>. Works on parsed JSON because
    /// PostgreSQL normalises <c>jsonb</c> (key order/whitespace) â€” string equality is never safe here.
    /// </summary>
    public static string MergeUndoWarnings(string? appliedSnapshot, IReadOnlyList<string> warnings)
    {
        var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var parsed = ParseJson(appliedSnapshot);

        if (parsed is { ValueKind: JsonValueKind.Object } root)
        {
            foreach (var property in root.EnumerateObject())
            {
                merged[property.Name] = property.Value.Clone();
            }
        }

        merged["undoWarnings"] = JsonSerializer.SerializeToElement(warnings, Json);
        return JsonSerializer.Serialize(merged, Json);
    }

    /// <summary>Defensive jsonb parse: absent/invalid JSON â‡’ null (never throws).</summary>
    public static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Number of sub-tasks in an <c>after_snapshot</c>; tolerant (0 when unreadable).</summary>
    public static int CountTasks(string? afterSnapshotJson)
    {
        if (!TryGetProperty(afterSnapshotJson, "tasks", out var tasks)
            || tasks.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return tasks.GetArrayLength();
    }

    /// <summary>Reads a GUID array (camelCase) out of a snapshot such as <c>applied_snapshot</c>.</summary>
    public static IReadOnlyList<Guid> ReadGuidArray(string? snapshotJson, string propertyName)
    {
        if (!TryGetProperty(snapshotJson, propertyName, out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var ids = new List<Guid>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>
    /// Reads one GUID out of a snapshot (camelCase). Used by the Phase 7 appliers to find what they
    /// wrote (<c>createdCommentId</c>/<c>createdAttachmentId</c>) when Undo runs later.
    /// </summary>
    public static Guid? ReadGuid(string? snapshotJson, string propertyName)
        => TryGetProperty(snapshotJson, propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
           && Guid.TryParse(value.GetString(), out var id)
            ? id
            : null;

    /// <summary>Reads one string out of a snapshot (camelCase); absent/blank ⇒ <c>null</c>.</summary>
    public static string? ReadString(string? snapshotJson, string propertyName)
        => TryGetProperty(snapshotJson, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static AiActionLogResponse BuildResponse(
        AiActionLog log, string? requestedByName, string? decidedByName = null)
        => new(log.Id, log.Action, log.EntityType, log.EntityId, log.Status.ToString(),
            log.RequestedByUserId, requestedByName,
            log.DecidedByUserId, decidedByName, log.DecidedAt, log.DecisionNote,
            CountTasks(log.AfterSnapshot), log.CreatedAt, log.UpdatedAt);

    public static AiActionLogDetailResponse BuildDetailResponse(
        AiActionLog log, string? requestedByName, string? decidedByName = null)
        => new(log.Id, log.Action, log.EntityType, log.EntityId, log.Status.ToString(),
            log.RequestedByUserId, requestedByName,
            log.DecidedByUserId, decidedByName, log.DecidedAt, log.DecisionNote,
            ParseJson(log.Basis), ParseJson(log.BeforeSnapshot), ParseJson(log.AfterSnapshot),
            ParseJson(log.AppliedSnapshot),
            ReadGuidArray(log.AppliedSnapshot, "createdTaskIds"),
            log.CreatedAt, log.UpdatedAt);

    private static bool TryGetProperty(string? json, string propertyName, out JsonElement value)
    {
        value = default;
        var parsed = ParseJson(json);
        if (parsed is not { ValueKind: JsonValueKind.Object } root)
        {
            return false;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string? CapLength(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];
}
