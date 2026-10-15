using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using TeamNexus.Modules.Ai.Contracts;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Ai.Services.Agent;
using TeamNexus.Modules.Ai.Services.Appliers;
using TeamNexus.Modules.Ai.Services.Jev;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;
using BoardEntity = TeamNexus.Persistence.Data.Entities.Board;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// The AI Observer's periodic scan (Phase 5 §4.5): load → detect → summarize → ask the AI →
/// validate → notify. Read-only with respect to business data: it never writes
/// <c>tasks</c>/<c>labels</c>/<c>task_labels</c> and never goes through the Accountability Layer —
/// the Observer is an *alerting* layer, not a *writing* one (Phase 4 §6 handover).
/// </summary>
public interface IObserverService
{
    /// <summary>
    /// Scans one workspace (manual trigger) or every active workspace (background run).
    /// A manual call runs even when <c>Observer:Enabled</c> is false so the feature can be demoed
    /// and verified; a background call is skipped in that case.
    /// <para>
    /// When <paramref name="actingUserId"/> is supplied the caller must be Manager/Admin of the
    /// target workspace (403 otherwise; unknown workspace ⇒ 404). The HTTP endpoint always passes
    /// it; the background timer passes <c>null</c> because it runs on behalf of the system.
    /// </para>
    /// </summary>
    Task<ObserverScanOutcome> ScanAsync(Guid? workspaceId, Guid? actingUserId = null, CancellationToken ct = default);

    /// <summary>Run history of one workspace (Manager/Admin only).</summary>
    Task<IReadOnlyList<ObserverRunResponse>> ListRunsAsync(
        Guid workspaceId, int take, Guid userId, CancellationToken ct = default);

    /// <summary>One run with its summary and findings (Manager/Admin of its workspace).</summary>
    Task<ObserverRunDetailResponse> GetRunAsync(Guid runId, Guid userId, CancellationToken ct = default);
}

public sealed class ObserverService : IObserverService
{
    /// <summary>Fixed advisory-lock key ("TN_OBSVR"): changing it lets two app versions scan at once.</summary>
    private const long AdvisoryLockKey = 0x544E5F4F42535652;

    public const int DefaultRunsTake = 20;

    public const int MaxRunsTake = 50;

    /// <summary>In-process guard complementing the database advisory lock (§4.5 step 2).</summary>
    private static readonly SemaphoreSlim LocalGate = new(1, 1);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IAiProvider _aiProvider;
    private readonly INotificationService _notifications;
    private readonly ObserverOptions _options;
    private readonly IJevDecisionService _jev;
    private readonly Jev.JevOptions _jevOptions;
    private readonly IAiActionService _actions;
    private readonly ILogger<ObserverService> _logger;

    /// <summary>
    /// Model name recorded in the run summary. The Observer must stay provider-agnostic (the port
    /// has no model concept), so this is derived from the DeepSeek configuration and falls back to
    /// the active provider's type name when no key is configured (i.e. the offline provider).
    /// </summary>
    private readonly string _providerModel;

    public ObserverService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IAiProvider aiProvider,
        INotificationService notifications,
        IOptions<ObserverOptions> options,
        IOptions<DeepSeekOptions> aiOptions,
        IJevDecisionService jev,
        IOptions<Jev.JevOptions> jevOptions,
        IAiActionService actions,
        ILogger<ObserverService> logger)
    {
        _db = db;
        _access = access;
        _aiProvider = aiProvider;
        _notifications = notifications;
        _options = options.Value;
        _jev = jev;
        _jevOptions = jevOptions.Value;
        _actions = actions;
        _logger = logger;
        _providerModel = aiOptions.Value.HasApiKey ? aiOptions.Value.Model : aiProvider.GetType().Name;
    }

    // ---- scan --------------------------------------------------------------

    public async Task<ObserverScanOutcome> ScanAsync(
        Guid? workspaceId, Guid? actingUserId = null, CancellationToken ct = default)
    {
        var runId = Guid.NewGuid();

        // 0) A manual, workspace-scoped scan is a Manager/Admin action (404 unknown workspace,
        //    403 for a plain member). Background runs pass no actor and skip this, since they are
        //    system-initiated and cover every workspace.
        if (workspaceId.HasValue && actingUserId.HasValue)
        {
            await RequireWorkspaceManagerAsync(workspaceId.Value, actingUserId.Value, ct);
        }

        // 1) Disabled + no explicit target ⇒ never touch the database. A manual scan still runs.
        if (!_options.Enabled && workspaceId is null)
        {
            _logger.LogInformation("Observer scan skipped: Observer:Enabled=false.");
            return Skipped(runId, workspaceId ?? Guid.Empty, "Disabled");
        }

        // 2) Only one scan at a time, in this process and across processes.
        if (!await LocalGate.WaitAsync(TimeSpan.Zero, ct))
        {
            _logger.LogWarning("Observer scan skipped: another scan is already running in this process.");
            return Skipped(runId, workspaceId ?? Guid.Empty, "AlreadyRunning");
        }

        try
        {
            if (!await TryAcquireAdvisoryLockAsync(ct))
            {
                _logger.LogWarning("Observer scan skipped: another instance holds the advisory lock.");
                return Skipped(runId, workspaceId ?? Guid.Empty, "AlreadyRunning");
            }

            try
            {
                var targets = await LoadTargetsAsync(workspaceId, ct);
                if (targets.Count == 0)
                {
                    _logger.LogInformation("Observer scan found no workspace with active tasks to scan.");
                    return Skipped(runId, workspaceId ?? Guid.Empty, "NoWorkspaces");
                }

                ObserverScanOutcome? last = null;
                foreach (var target in targets)
                {
                    ct.ThrowIfCancellationRequested();
                    last = await ScanWorkspaceAsync(target.Id, runId, ct);
                }

                // Single-workspace scans return that workspace's outcome; a full run reports the
                // last workspace scanned (each workspace keeps its own run row).
                return last ?? Skipped(runId, workspaceId ?? Guid.Empty, "NoWorkspaces");
            }
            finally
            {
                await ReleaseAdvisoryLockAsync();
            }
        }
        finally
        {
            LocalGate.Release();
        }
    }

    /// <summary>One workspace: load → detect → ask AI → validate → notify → record the run.</summary>
    private async Task<ObserverScanOutcome> ScanWorkspaceAsync(Guid workspaceId, Guid runId, CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        var snapshot = await LoadSnapshotAsync(workspaceId, ct);
        var signals = ObserverSignalDetector.Analyze(snapshot, _options.ToThresholds());

        var signalsByType = signals.Signals
            .GroupBy(s => s.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        var aiCalled = false;
        var findings = new List<ObserverFinding>();
        string? error = null;

        // Giai đoạn 14.7 Track A / ISSUE-01: a failure of the System 2 (prose) step is recorded HERE and not
        // in `error`. See the remarks on the LLM block below for why that distinction is the whole fix.
        string? detailError = null;
        var usedDetectorFallback = false;
        var skippedReason = (string?)null;
        string? model = null;
        int? promptTokens = null;
        int? completionTokens = null;
        var truncatedByPrompt = 0;

        // Giai đoạn 14.7 §A5 Phần A: the System One verdict, when the Jev path is on.
        ObserverRiskAssessmentResult? jevRisk = null;
        var jevUsed = false;

        if (signals.Signals.Count == 0)
        {
            // 5) Nothing to interpret ⇒ never spend a token (roadmap requirement).
            skippedReason = "NoSignals";
        }
        else
        {
            // 5a) Giai đoạn 14.7 §A5 Phần A — ask Jev FIRST, when the flag is on.
            //
            // Deliberately gated by its own setting rather than by Jev:Enabled: the Observer runs on a
            // timer, so a global off-switch must not make every tick throw. With the flag on and Jev
            // unreachable, the assessment degrades to ManualRequired and the LLM path below runs unchanged —
            // which is exactly the "Jev is an optimisation, not a dependency" contract.
            if (_jevOptions.ObserverSeverityRouting)
            {
                try
                {
                    // The workspace NAME, never its id: see BuildRiskContext.
                    var workspaceName = await LoadWorkspaceNameAsync(workspaceId, ct);

                    jevRisk = await _jev.AssessObserverSignalsAsync(
                        BuildRiskContext(snapshot, signals, workspaceName), ct);
                    jevUsed = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // `AssessObserverSignalsAsync` already absorbs provider failures, so reaching here means
                    // something unexpected. Degrade to "no verdict" rather than failing the scan: the detector
                    // output below is still a perfectly good alert set.
                    _logger.LogWarning(
                        ex,
                        "Jev severity assessment failed for workspace {WorkspaceId}; continuing without it.",
                        workspaceId);
                }

                // 5b) Severity gating. A *confident* judgment that the situation is below the alert floor
                // skips the LLM entirely — that is the token/cost win the phase claims, and it is only
                // taken when the confidence is high enough to act on. A low-confidence verdict falls
                // through to the LLM, which is the System 2 fallback the roadmap describes.
                if (jevRisk is not null
                    && jevRisk.RoutingAdvice == Jev.ConfidenceRouting.DirectAssign
                    && !NotificationSeverities.AtLeast(jevRisk.EvaluatedSeverity, _options.MinSeverityToNotify))
                {
                    skippedReason = "JevBelowThreshold";
                }
            }

            if (skippedReason is null)
            {
                // 6–7) Summarize, then one non-retrying completion call. The Jev verdict is handed down as
                // a directive so the model explains at the severity our own code already decided.
                var directive = jevRisk is null
                    ? null
                    : ObserverPrompts.BuildJevSeverityDirective(
                        jevRisk.EvaluatedSeverity, jevRisk.RequireImmediateAlert, jevRisk.HasDominoRisk);

                var request = ObserverSummarizer.BuildRequest(snapshot, signals, _options, directive);
                truncatedByPrompt = Math.Max(
                    0,
                    signals.Signals.Count - ObserverSummarizer.CountSignalsInPayload(request.UserPrompt));

                _logger.LogDebug(
                    "Observer prompt for workspace {WorkspaceId}: {Length} ký tự, {Signals} tín hiệu (jev={Jev}).",
                    workspaceId,
                    request.UserPrompt.Length,
                    signals.Signals.Count,
                    jevUsed);

                aiCalled = true;

                // ⚠️ **ISSUE-01 fix — the prose step is now its own failure domain.**
                //
                // Before this, an `AiProviderException` from here was caught by an OUTER handler that set
                // `error`, which (a) marked the whole run `Failed`, (b) discarded Jev's severity verdict that
                // had ALREADY been computed successfully, and (c) skipped the notification block, so a
                // DeepSeek timeout produced "no signals, no alerts" for a workspace that genuinely had
                // overdue tasks. A provider outage in the *explanation* step must not erase the *detection*
                // step, which needs no AI at all.
                //
                // The detector's own output is therefore used as the alert set when the model is unavailable:
                // each signal already carries a type, a severity and a human-readable summary composed from
                // real data, so the alert is honest — and `detailError` records that the richer explanation
                // never arrived.
                try
                {
                    var completion = await _aiProvider.CompleteAsync(request, ct);
                    promptTokens = completion.PromptTokens;
                    completionTokens = completion.CompletionTokens;
                    model = _providerModel;

                    var output = ParseOutput(completion.Content);

                    // 8) Anti-hallucination boundary. Unchanged, and deliberately so: Jev's severity is an INPUT
                    // to the prompt, never a bypass of the validator that decides which findings are written.
                    findings = ObserverFindingValidator.Validate(output, signals.Signals, _options).ToList();
                }
                catch (AiProviderException ex)
                {
                    detailError = Cap(ex.Message, 500);
                    findings = BuildDetectorFindings(signals.Signals, jevRisk);
                    usedDetectorFallback = true;

                    _logger.LogWarning(
                        ex,
                        "Observer detail step failed for workspace {WorkspaceId}; falling back to detector-derived "
                        + "findings ({Findings}).",
                        workspaceId,
                        findings.Count);
                }
                catch (JsonException ex)
                {
                    detailError = Cap(ex.Message, 500);
                    findings = BuildDetectorFindings(signals.Signals, jevRisk);
                    usedDetectorFallback = true;

                    _logger.LogWarning(
                        ex,
                        "Observer detail step returned unparsable output for workspace {WorkspaceId}; falling back "
                        + "to detector-derived findings ({Findings}).",
                        workspaceId,
                        findings.Count);
                }
            }
        }

        // The run only counts as Failed when we produced NOTHING usable. A completed scan with a degraded
        // explanation is still a successful scan: the manager gets real alerts, and the run summary says the
        // prose step did not happen.
        var status = error is not null ? ObserverRunStatus.Failed : ObserverRunStatus.Completed;

        // 9) Notify (only on a successful run), then record the run + prune old activity.
        var notificationsCreated = 0;
        if (status == ObserverRunStatus.Completed && findings.Count > 0)
        {
            var context = new ObserverRunContext(runId, model ?? "unspecified", promptTokens, completionTokens);
            notificationsCreated = await _notifications.NotifyManagersAsync(workspaceId, findings, context, ct);
        }

        // 9b) Giai đoạn 14.7 §A5 Phần B — the proactive half. A High/Critical situation gets a concrete
        // recovery PLAN as a Pending action, so the manager approves a solution instead of reading an alert
        // and starting from scratch.
        //
        // Runs AFTER the notifications: the alert is the primary output and must never be lost because the
        // optional proposal failed. Any failure here is logged and swallowed for the same reason.
        var recoveryPlanLogId = await TryDraftRecoveryPlanAsync(
            workspaceId, snapshot, signals, findings, jevRisk, model, ct);

        stopwatch.Stop();

        await PersistRunAsync(
            workspaceId, runId, startedAt, status, signals, signalsByType, findings, notificationsCreated,
            aiCalled, model, promptTokens, completionTokens, truncatedByPrompt, skippedReason, error,
            stopwatch.ElapsedMilliseconds, ct,
            jevUsed, jevRisk, recoveryPlanLogId, detailError, usedDetectorFallback);

        if (status == ObserverRunStatus.Completed)
        {
            await PruneActivityAsync(workspaceId, ct);
        }

        _logger.LogInformation(
            "Observer run {RunId} for workspace {WorkspaceId}: status={Status}, signals={Signals}, "
            + "findings={Findings}, notifications={Notifications}, aiCalled={AiCalled}, durationMs={DurationMs}, "
            + "detailError={HasDetailError}, detectorFallback={Fallback}.",
            runId,
            workspaceId,
            status,
            signals.Signals.Count,
            findings.Count,
            notificationsCreated,
            aiCalled,
            stopwatch.ElapsedMilliseconds,
            detailError is not null,
            usedDetectorFallback);

        return new ObserverScanOutcome(
            runId, workspaceId, status.ToString(), signals.Signals.Count, findings.Count,
            notificationsCreated, aiCalled, skippedReason, error,
            detailError, usedDetectorFallback);
    }

    // ---- run history -------------------------------------------------------

    public async Task<IReadOnlyList<ObserverRunResponse>> ListRunsAsync(
        Guid workspaceId, int take, Guid userId, CancellationToken ct = default)
    {
        await RequireWorkspaceManagerAsync(workspaceId, userId, ct);

        var limit = take <= 0 ? DefaultRunsTake : Math.Clamp(take, 1, MaxRunsTake);

        var runs = await _db.AiObserverRuns
            .AsNoTracking()
            .Where(r => r.WorkspaceId == workspaceId)
            .OrderByDescending(r => r.StartedAt)
            .Take(limit)
            .ToListAsync(ct);

        return runs.Select(ToRunResponse).ToList();
    }

    public async Task<ObserverRunDetailResponse> GetRunAsync(
        Guid runId, Guid userId, CancellationToken ct = default)
    {
        var run = await _db.AiObserverRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new NotFoundException("Observer run not found.");

        await RequireWorkspaceManagerAsync(run.WorkspaceId, userId, ct);

        return new ObserverRunDetailResponse(
            run.Id, run.WorkspaceId, run.Status.ToString(), run.StartedAt, run.FinishedAt,
            AiActionService.ParseJson(run.Summary),
            ReadFindings(run.Summary).Select(ObserverRunFinding.From).ToList());
    }

    // ---- Jev integration (Giai đoạn 14.7 §A5) ------------------------------

    /// <summary>
    /// Shapes the detector's output into the typed context Jev sees. Pure.
    /// <para>
    /// <b>Evidence ids are deliberately NOT carried</b>, and neither is the workspace id — the caller supplies
    /// the human-readable <paramref name="workspaceName"/>. A model that receives no identifier cannot echo
    /// one, which is the structural reason this path can claim 0 % leakage without the filtering the LLM path
    /// needs. An earlier version of this method passed <c>snapshot.WorkspaceId.ToString()</c> here, which
    /// would have handed Jev exactly the identifier the design exists to withhold.
    /// </para>
    /// </summary>
    internal static ObserverRiskContext BuildRiskContext(
        ObserverWorkspaceSnapshot snapshot, ObserverSignalSet signals, string workspaceName)
    {
        var open = snapshot.Tasks.Where(t => !t.IsDoneColumn).ToList();

        return new ObserverRiskContext(
            WorkspaceName: workspaceName,
            OpenTaskCount: open.Count,
            OverdueTaskCount: open.Count(t => t.DueDate.HasValue && t.DueDate.Value < snapshot.Now),
            StalledTaskCount: signals.Signals
                .Where(s => string.Equals(s.Type, ObserverSignalDetector.StalledTask, StringComparison.Ordinal))
                .Sum(s => s.TaskIds.Count),
            AtRiskTaskCount: signals.Signals
                .Where(s => string.Equals(s.Type, ObserverSignalDetector.AtRiskDeadline, StringComparison.Ordinal))
                .Sum(s => s.TaskIds.Count),
            OverloadedMemberCount: signals.Signals
                .Count(s => string.Equals(s.Type, ObserverSignalDetector.Overload, StringComparison.Ordinal)),
            BoardCount: snapshot.Columns.Select(c => c.BoardId).Distinct().Count(),
            Signals: signals.Signals
                .Select(signal => new ObserverSignalContext(
                    signal.Type, signal.Severity, signal.Weight, signal.Summary,
                    signal.TaskIds.Count, signal.UserIds.Count))
                .ToList());
    }

    /// <summary>
    /// Drafts one Pending recovery plan when a confident Jev verdict says the situation is High or Critical
    /// (Giai đoạn 14.7 §A5 Phần B).
    /// <para>
    /// Returns the created log id, or <c>null</c> when no plan was drafted. <b>Never throws</b>: the run's
    /// alerts are its primary output, so a failed proposal must degrade to "alert only" — the same
    /// fail-soft rule as <c>IExternalAlertSink</c> and the activity-log writer.
    /// </para>
    /// <para>
    /// <b>The requester is a manager, not "the system".</b> <c>ai_action_logs.requested_by_user_id</c> is a
    /// required FK to <c>users</c> and there is no system principal, so the draft is attributed to the
    /// longest-standing Admin/Manager — the person who can actually approve it. Inventing a synthetic system
    /// user would need a schema change and would make the audit trail lie about who proposed the action.
    /// </para>
    /// </summary>
    private async Task<Guid?> TryDraftRecoveryPlanAsync(
        Guid workspaceId,
        ObserverWorkspaceSnapshot snapshot,
        ObserverSignalSet signals,
        IReadOnlyList<ObserverFinding> findings,
        ObserverRiskAssessmentResult? jevRisk,
        string? model,
        CancellationToken ct)
    {
        if (!_jevOptions.AutoRecoveryPlanEnabled || !_jevOptions.Enabled)
        {
            return null;
        }

        // A confident High/Critical verdict only. An unusable (low-confidence) assessment means our code
        // cannot claim the situation is severe, and drafting a data-affecting proposal on a guess is exactly
        // what the confidence floors exist to prevent.
        if (jevRisk is null || jevRisk.RoutingAdvice == Jev.ConfidenceRouting.ManualRequired)
        {
            return null;
        }

        if (!NotificationSeverities.AtLeast(jevRisk.EvaluatedSeverity, ObserverSeverity.High))
        {
            return null;
        }

        var target = ResolveRecoveryTarget(snapshot, signals);
        if (target is null)
        {
            return null;
        }

        var requester = await LoadDraftRequesterAsync(workspaceId, ct);
        if (requester is null)
        {
            _logger.LogInformation(
                "Recovery plan skipped for workspace {WorkspaceId}: no Admin/Manager to attribute it to.",
                workspaceId);
            return null;
        }

        try
        {
            var candidateAssigneeCount = await _db.WorkspaceMembers
                .AsNoTracking()
                .CountAsync(wm => wm.WorkspaceId == workspaceId, ct);

            var decision = await _jev.DecideRecoveryActionAsync(
                new RecoveryActionContext(
                    TaskTitle: target.Title,
                    TaskDescription: null,
                    SignalType: target.SignalType,
                    EvaluatedSeverity: jevRisk.EvaluatedSeverity,
                    DaysOverdue: target.DaysOverdue,
                    DaysRemaining: target.DaysRemaining,
                    OpenTaskCountOfAssignee: target.AssigneeOpenTaskCount,
                    CandidateAssigneeCount: candidateAssigneeCount,
                    Options: RecoveryOptions),
                ct);

            if (decision.ActionType is null || decision.RoutingAdvice == Jev.ConfidenceRouting.ManualRequired)
            {
                return null;
            }

            // Task-scoped kinds need the card; EscalateManager does not, so it may be drafted even when no
            // single card is the culprit (e.g. a workspace-wide Overload signal).
            var payload = BuildRecoveryPayload(decision.ActionType, jevRisk, findings, signals, target, model);

            var log = await _actions.RequestRecoveryPlanAsync(
                workspaceId, requester.Value,
                JsonSerializer.Serialize(payload, Json),
                JsonSerializer.Serialize(new
                {
                    source = "Observer",
                    signalType = target.SignalType,
                    candidateCount = signals.Signals.Count,
                    findingCount = findings.Count,
                    jevSeverity = jevRisk.EvaluatedSeverity,
                    jevConfidence = jevRisk.ConfidenceScore,
                    requestedAt = DateTimeOffset.UtcNow,
                }, Json),
                ct);

            // Tell the managers something is waiting for a decision. Without this the proposal would only be
            // discoverable by opening the AI history, which is not a notification.
            await _notifications.NotifyManagersAsync(
                workspaceId,
                new AgentNotification(
                    AgentNotificationTypes.RecoveryPlanPending,
                    $"Kịch bản phục hồi đang chờ duyệt: {target.Title}",
                    BuildRecoveryMessage(decision.ActionType, jevRisk, target),
                    JsonSerializer.Serialize(new
                    {
                        aiActionLogId = log.Id,
                        recoveryKind = decision.ActionType,
                        severity = jevRisk.EvaluatedSeverity,
                        confidence = jevRisk.ConfidenceScore,
                        taskId = target.TaskId,
                    }, Json)),
                ct);

            _logger.LogInformation(
                "Recovery plan drafted for workspace {WorkspaceId}: kind={Kind}, severity={Severity}, log={LogId}.",
                workspaceId, decision.ActionType, jevRisk.EvaluatedSeverity, log.Id);

            return log.Id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail-soft: the alerts are already written and must stay the run's outcome.
            _logger.LogWarning(
                ex,
                "Could not draft a recovery plan for workspace {WorkspaceId}; the Observer alerts are unaffected.",
                workspaceId);

            return null;
        }
    }

    /// <summary>
    /// The four recovery kinds offered to Jev, with the descriptions the model chooses between
    /// (Giai đoạn 14.7 §A5 Phần B). Kept here rather than in the prompt so the option keys and the applier's
    /// switch stay one vocabulary.
    /// </summary>
    internal static IReadOnlyList<RecoveryActionOption> RecoveryOptions { get; } =
    [
        new(RecoveryPlanKinds.ReassignTask, "Chuyển thẻ sang thành viên còn khả năng nhận việc."),
        new(RecoveryPlanKinds.SplitSubtasks, "Giữ thẻ nhưng phân rã phần tồn đọng thành các thẻ con nhỏ hơn."),
        new(RecoveryPlanKinds.ExtendDeadline, "Gia hạn hạn chót tới một mốc khả thi theo tiến độ thực tế."),
        new(RecoveryPlanKinds.EscalateManager, "Không thể tự giải quyết — cần quản lý họp khẩn."),
    ];

    /// <summary>
    /// Picks the card a recovery plan should target, plus the numbers the decision needs. Prefers the most
    /// overdue card, then the most at-risk one, then the most stalled — the order the detectors rank severity in.
    /// </summary>
    private static RecoveryTarget? ResolveRecoveryTarget(
        ObserverWorkspaceSnapshot snapshot, ObserverSignalSet signals)
    {
        var byTaskId = snapshot.Tasks.ToDictionary(t => t.TaskId);

        foreach (var type in new[]
                 {
                     ObserverSignalDetector.OverdueTask,
                     ObserverSignalDetector.AtRiskDeadline,
                     ObserverSignalDetector.StalledTask,
                 })
        {
            var signal = signals.Signals
                .FirstOrDefault(s => string.Equals(s.Type, type, StringComparison.Ordinal));

            // Evidence ids arrive already sorted by the detector (worst first), so the head is the best target.
            var taskId = signal?.TaskIds.FirstOrDefault();
            if (taskId is null || !byTaskId.TryGetValue(taskId.Value, out var task))
            {
                continue;
            }

            var overdueDays = task.DueDate.HasValue && task.DueDate.Value < snapshot.Now
                ? (int)(snapshot.Now - task.DueDate.Value).TotalDays
                : 0;

            var remainingDays = task.DueDate.HasValue && task.DueDate.Value >= snapshot.Now
                ? (int)(task.DueDate.Value - snapshot.Now).TotalDays
                : 0;

            var assigneeOpen = task.AssigneeId.HasValue
                ? snapshot.Tasks.Count(t => t.AssigneeId == task.AssigneeId && !t.IsDoneColumn)
                : 0;

            return new RecoveryTarget(
                task.TaskId, task.BoardId, task.Title, type, overdueDays, remainingDays, assigneeOpen);
        }

        return null;
    }

    /// <summary>
    /// The stored plan. Task-scoped kinds carry the card; <c>SplitSubtasks</c> carries a single starter
    /// sub-task so the applier has something valid to create, and <c>EscalateManager</c> carries neither —
    /// its whole meaning is that no automatic change is appropriate.
    /// </summary>
    private static RecoveryPlanPayload BuildRecoveryPayload(
        string kind,
        ObserverRiskAssessmentResult risk,
        IReadOnlyList<ObserverFinding> findings,
        ObserverSignalSet signals,
        RecoveryTarget target,
        string? model)
    {
        var summary = $"[{risk.EvaluatedSeverity}] {target.SignalType} — {target.Title}";

        if (string.Equals(kind, RecoveryPlanKinds.EscalateManager, StringComparison.Ordinal))
        {
            return new RecoveryPlanPayload(kind, summary, BoardId: target.BoardId);
        }

        if (string.Equals(kind, RecoveryPlanKinds.SplitSubtasks, StringComparison.Ordinal))
        {
            // One sub-task, deliberately minimal: the manager edits the proposal before approving it, and
            // generating a whole breakdown here would mean a second model call for a plan nobody has
            // confirmed. The task's own title is the honest basis for the split.
            return new RecoveryPlanPayload(
                kind,
                summary,
                TaskId: target.TaskId,
                BoardId: target.BoardId,
                ExtensionDays: null,
                Tasks:
                [
                    new SmartSetupTaskProposal(
                        Title: $"Xử lý phần tồn đọng: {Cap(target.Title, 120)}",
                        Description: $"Tách từ thẻ \"{target.Title}\" do tín hiệu {target.SignalType} "
                                     + $"(mức {risk.EvaluatedSeverity}).",
                        Priority: "High",
                        Labels: [],
                        Assignee: null),
                ]);
        }

        if (string.Equals(kind, RecoveryPlanKinds.ExtendDeadline, StringComparison.Ordinal))
        {
            // A bounded, honest extension: two weeks is the smallest window that gives a stalled card room to
            // move, and the applier clamps whatever arrives.
            return new RecoveryPlanPayload(kind, summary, TaskId: target.TaskId, ExtensionDays: 14, BoardId: target.BoardId);
        }

        return new RecoveryPlanPayload(kind, summary, TaskId: target.TaskId, BoardId: target.BoardId);
    }

    private static string BuildRecoveryMessage(string kind, ObserverRiskAssessmentResult risk, RecoveryTarget target)
        => kind switch
        {
            RecoveryPlanKinds.ReassignTask =>
                $"AI đề xuất chuyển thẻ \"{target.Title}\" sang người còn khả năng nhận việc. "
                + $"Mức {risk.EvaluatedSeverity}, độ tin cậy {risk.ConfidenceScore:P0}.",
            RecoveryPlanKinds.SplitSubtasks =>
                $"AI đề xuất phân rã phần tồn đọng của thẻ \"{target.Title}\" thành thẻ con. "
                + $"Mức {risk.EvaluatedSeverity}, độ tin cậy {risk.ConfidenceScore:P0}.",
            RecoveryPlanKinds.ExtendDeadline =>
                $"AI đề xuất gia hạn hạn chót của thẻ \"{target.Title}\". "
                + $"Mức {risk.EvaluatedSeverity}, độ tin cậy {risk.ConfidenceScore:P0}.",
            _ =>
                $"AI đánh giá tình hình ở mức {risk.EvaluatedSeverity} và cho rằng không thể tự giải quyết — "
                + "cần họp khẩn. Không có thay đổi dữ liệu nào được đề xuất.",
        };

    /// <summary>
    /// The workspace's display name for the Jev state. Read once per scan; falls back to a generic word so a
    /// missing row can never produce a <c>null</c> state field (which would fail the provider's validation).
    /// </summary>
    private async Task<string> LoadWorkspaceNameAsync(Guid workspaceId, CancellationToken ct)
        => await _db.Workspaces
               .AsNoTracking()
               .Where(w => w.Id == workspaceId)
               .Select(w => w.Name)
               .FirstOrDefaultAsync(ct)
           ?? "workspace";

    /// <summary>
    /// The user the draft is attributed to: the longest-standing Admin, else the longest-standing Manager.
    /// Deterministic (ordered by <c>JoinedAt</c>, then id) so the same workspace always names the same person.
    /// </summary>
    private async Task<Guid?> LoadDraftRequesterAsync(Guid workspaceId, CancellationToken ct)
        => await _db.WorkspaceMembers
            .AsNoTracking()
            .Where(wm => wm.WorkspaceId == workspaceId
                         && (wm.Role == WorkspaceRole.Admin || wm.Role == WorkspaceRole.Manager))
            .OrderBy(wm => wm.Role == WorkspaceRole.Admin ? 0 : 1)
            .ThenBy(wm => wm.JoinedAt)
            .ThenBy(wm => wm.UserId)
            .Select(wm => (Guid?)wm.UserId)
            .FirstOrDefaultAsync(ct);

    /// <summary>The card a plan targets, with the numbers the decision needs.</summary>
    private sealed record RecoveryTarget(
        Guid TaskId,
        Guid BoardId,
        string Title,
        string SignalType,
        int DaysOverdue,
        int DaysRemaining,
        int AssigneeOpenTaskCount);

    /// <summary>
    /// Builds an alert set from the detector's own signals — the fallback used when the System 2 (prose) step
    /// is unavailable (Giai đoạn 14.7 Track A, **ISSUE-01**).
    /// <para>
    /// <b>Why this is not "inventing findings".</b> Every field comes from the detector, which is plain
    /// deterministic C# over real rows: the <c>Type</c> is the signal's type, <c>TaskIds</c>/<c>UserIds</c> are
    /// the evidence ids the detector already capped, and the message is composed from the signal's own
    /// summary. Nothing here is model output, which is why no validator pass is needed — and why the
    /// anti-hallucination guarantee is not weakened: there is no model in this path to hallucinate.
    /// </para>
    /// <para>
    /// <b>Why not simply write no alert at all.</b> That was the old behaviour and it is the actual harm of
    /// the issue: a DeepSeek timeout produced "0 signals, 0 alerts" for a workspace that genuinely had four
    /// overdue tasks, so an outage of the *explanation* step silently disabled *detection*.
    /// </para>
    /// <para>
    /// <b>Severity:</b> each finding keeps its OWN signal's severity rather than the run-level verdict from
    /// Jev. A single assessed severity describes the whole situation, so copying it onto every signal would
    /// relabel a Medium bottleneck as Critical — a worse outcome than losing the aggregate.
    /// </para>
    /// <para>
    /// Public for the same reason the other pure helpers are: the fallback's shape is the interesting part,
    /// and a test must be able to pin it without a database or a failing provider.
    /// </para>
    /// </summary>
    public static List<ObserverFinding> BuildDetectorFindings(
        IReadOnlyList<ObserverSignal> signals, ObserverRiskAssessmentResult? jevRisk)
    {
        var findings = new List<ObserverFinding>(signals.Count);

        foreach (var signal in signals)
        {
            // A signal whose severity is somehow unknown is skipped rather than forced to Medium: the
            // persistence layer relies on this value being one of the four canonical words.
            if (!NotificationSeverities.IsKnown(signal.Severity))
            {
                continue;
            }

            var dominoNote = jevRisk?.HasDominoRisk == true
                ? " Mô hình đánh giá có nguy cơ đổ vỡ dây chuyền."
                : string.Empty;

            findings.Add(new ObserverFinding(
                Type: signal.Type,
                Severity: signal.Severity,
                Title: $"Cảnh báo tiến độ: {signal.Type}",
                Message: $"{signal.Summary} Hệ thống phát hiện tín hiệu này trực tiếp từ dữ liệu bảng."
                         + $" Phần giải thích chi tiết của AI tạm thời không khả dụng.{dominoNote}",
                TaskIds: signal.TaskIds,
                UserIds: signal.UserIds));
        }

        return findings;
    }

    // ---- data loading ------------------------------------------------------

    /// <summary>
    /// Workspaces to scan: active ones with at least one open task, most recently active first,
    /// capped by <c>MaxWorkspacesPerRun</c> (cost control).
    /// </summary>
    private async Task<IReadOnlyList<WorkspaceTarget>> LoadTargetsAsync(Guid? workspaceId, CancellationToken ct)
    {
        var candidates = await _db.Boards
            .AsNoTracking()
            .Where(b => workspaceId == null || b.WorkspaceId == workspaceId)
            .Select(b => b.WorkspaceId)
            .Distinct()
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return [];
        }

        var activity = await _db.Activities
            .AsNoTracking()
            .Where(a => candidates.Contains(a.WorkspaceId))
            .GroupBy(a => a.WorkspaceId)
            .Select(g => new { WorkspaceId = g.Key, Last = g.Max(a => a.CreatedAt) })
            .ToListAsync(ct);

        var taskActivity = await _db.Tasks
            .AsNoTracking()
            .Where(t => candidates.Contains(t.Board!.WorkspaceId))
            .GroupBy(t => t.Board!.WorkspaceId)
            .Select(g => new { WorkspaceId = g.Key, Last = g.Max(t => t.UpdatedAt) })
            .ToListAsync(ct);

        var lastActivity = new Dictionary<Guid, DateTimeOffset>();
        foreach (var row in activity.Concat(taskActivity.Select(x => new { x.WorkspaceId, x.Last })))
        {
            if (!lastActivity.TryGetValue(row.WorkspaceId, out var current) || row.Last > current)
            {
                lastActivity[row.WorkspaceId] = row.Last;
            }
        }

        var openTaskWorkspaces = await _db.Tasks
            .AsNoTracking()
            .Where(t => candidates.Contains(t.Board!.WorkspaceId) && (t.Column == null || !t.Column.IsDone))
            .Select(t => t.Board!.WorkspaceId)
            .Distinct()
            .ToListAsync(ct);

        return openTaskWorkspaces
            .Select(id => new WorkspaceTarget(id, lastActivity.GetValueOrDefault(id, DateTimeOffset.MinValue)))
            .OrderByDescending(t => t.LastActivity)
            .ThenBy(t => t.Id)
            .Take(Math.Max(1, _options.MaxWorkspacesPerRun))
            .ToList();
    }

    /// <summary>Bounded read of one workspace's board state for the detector (Phase 5 §4.5 step 4).</summary>
    private async Task<ObserverWorkspaceSnapshot> LoadSnapshotAsync(Guid workspaceId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var boards = await _db.Boards
            .AsNoTracking()
            .Where(b => b.WorkspaceId == workspaceId)
            .Select(b => new { b.Id, b.Name })
            .ToListAsync(ct);

        var boardIds = boards.Select(b => b.Id).ToList();
        var boardNames = boards.ToDictionary(b => b.Id, b => b.Name);

        var columns = await _db.BoardColumns
            .AsNoTracking()
            .Where(c => boardIds.Contains(c.BoardId))
            .Select(c => new ObserverColumnSnapshot(c.Id, c.BoardId, c.Name, c.IsDone))
            .ToListAsync(ct);

        var tasks = await _db.Tasks
            .AsNoTracking()
            .Where(t => boardIds.Contains(t.BoardId))
            .Select(t => new
            {
                t.Id,
                t.BoardId,
                t.ColumnId,
                t.Title,
                t.AssigneeId,
                AssigneeName = t.Assignee != null ? t.Assignee.DisplayName : null,
                t.DueDate,
                t.CreatedAt,
                t.UpdatedAt,
                IsDone = t.Column != null && t.Column.IsDone,
            })
            .ToListAsync(ct);

        var taskIds = tasks.Select(t => t.Id).ToList();
        var commentStats = await _db.TaskComments
            .AsNoTracking()
            .Where(c => taskIds.Contains(c.TaskId))
            .GroupBy(c => c.TaskId)
            .Select(g => new { TaskId = g.Key, Count = g.Count(), Last = g.Max(c => c.CreatedAt) })
            .ToListAsync(ct);

        var statsByTask = commentStats.ToDictionary(x => x.TaskId);

        var snapshots = tasks.Select(t =>
        {
            var stats = statsByTask.GetValueOrDefault(t.Id);
            return new ObserverTaskSnapshot(
                t.Id,
                t.BoardId,
                boardNames.GetValueOrDefault(t.BoardId, string.Empty),
                t.ColumnId,
                columns.FirstOrDefault(c => c.ColumnId == t.ColumnId)?.Name ?? string.Empty,
                t.Title,
                t.AssigneeId,
                t.AssigneeName,
                t.DueDate,
                t.CreatedAt,
                t.UpdatedAt,
                t.IsDone,
                stats?.Count ?? 0,
                stats?.Last);
        }).ToList();

        return new ObserverWorkspaceSnapshot(workspaceId, snapshots, columns, now);
    }

    // ---- persistence -------------------------------------------------------

    private async Task PersistRunAsync(
        Guid workspaceId,
        Guid runId,
        DateTimeOffset startedAt,
        ObserverRunStatus status,
        ObserverSignalSet signals,
        Dictionary<string, int> signalsByType,
        IReadOnlyList<ObserverFinding> findings,
        int notificationsCreated,
        bool aiCalled,
        string? model,
        int? promptTokens,
        int? completionTokens,
        int truncatedByPrompt,
        string? skippedReason,
        string? error,
        long durationMs,
        CancellationToken ct,
        bool jevUsed = false,
        ObserverRiskAssessmentResult? jevRisk = null,
        Guid? recoveryPlanLogId = null,
        string? detailError = null,
        bool usedDetectorFallback = false)
    {
        var summary = JsonSerializer.Serialize(new
        {
            runId,
            signalsDetected = signals.Signals.Count,
            signalsByType,
            truncatedSignals = signals.TruncatedSignals,
            truncatedByPrompt,
            // Phase 14 §3.1 (P4): `signalsPreserved` is what the model actually received, so
            // `signalsDetected − signalsPreserved` is visible per run instead of being inferred. Without
            // it, adding a detector could silently starve that detector in large workspaces.
            signalsPreserved = Math.Max(0, signals.Signals.Count - truncatedByPrompt),
            signalsDroppedBeforePrompt = truncatedByPrompt,
            findingsWritten = findings.Count,
            notificationsCreated,
            aiCalled,
            model,
            promptTokens,
            completionTokens,
            // Giai đoạn 14.7 §A5: the System One evidence. `jevUsed` is false whenever the flag is off OR the
            // signals were empty, so a run's cost profile stays readable from its own summary.
            jevUsed,
            jevSeverity = jevRisk?.EvaluatedSeverity,
            jevConfidence = jevRisk?.ConfidenceScore,
            jevRequireImmediateAlert = jevRisk?.RequireImmediateAlert,
            jevHasDominoRisk = jevRisk?.HasDominoRisk,
            // Giai đoạn 14.7 Track A (ISSUE-05): the cost evidence. Jev bills per INPUT token with output
            // free, so `jevInputTokens` + `jevCostUsd` are what make "System 1 is ~90% cheaper" a measurement
            // instead of a claim. Read from the result's own usage block, which is why the usage is carried on
            // the result rather than dropped inside the decision service.
            jevInputTokens = jevRisk?.Usage?.InputTokens,
            jevOutputTokens = jevRisk?.Usage?.OutputTokens,
            jevCostUsd = jevRisk?.Usage?.CostUsd,
            // The id of the Pending plan this run drafted, if any — the audit link from an alert to the
            // proposal it produced.
            recoveryPlanLogId,
            // Giai đoạn 14.7 Track A / ISSUE-01: kept SEPARATE from `error` on purpose. `error` means the run
            // failed; this means the run succeeded but its prose step did not, which is a warning the UI shows
            // rather than a red failure state.
            detailError,
            usedDetectorFallback,
            durationMs,
            skippedReason,
            error,
            findings,
        }, Json);

        _db.AiObserverRuns.Add(new AiObserverRun
        {
            WorkspaceId = workspaceId,
            StartedAt = startedAt,
            FinishedAt = DateTimeOffset.UtcNow,
            Status = status,
            Summary = summary,
        });

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Retention for the activity event store (DB design §7): drops rows of this workspace older
    /// than <c>RetentionDays</c>, capped per pass. Executed as bulk delete — the table has no query
    /// filter, so nothing is hidden from this statement.
    /// <para>
    /// Scoped to the workspace being scanned on purpose: a run that reports on one workspace must
    /// not silently delete another workspace's history, and a globally unbounded batch could spend
    /// its whole budget on unrelated old rows and never reach this workspace's data.
    /// </para>
    /// </summary>
    private async Task PruneActivityAsync(Guid workspaceId, CancellationToken ct)
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, _options.RetentionDays));

            var deleted = await _db.Activities
                .Where(a => a.WorkspaceId == workspaceId && a.CreatedAt < cutoff)
                .OrderBy(a => a.CreatedAt)
                .Take(Math.Max(1, _options.RetentionDeleteBatchSize))
                .ExecuteDeleteAsync(ct);

            if (deleted > 0)
            {
                _logger.LogInformation(
                    "Observer retention: pruned {Deleted} activity_logs row(s) older than {Cutoff} (workspace {WorkspaceId}).",
                    deleted,
                    cutoff,
                    workspaceId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Retention must never fail a completed run.
            _logger.LogWarning(ex, "Observer retention prune failed (workspace {WorkspaceId}).", workspaceId);
        }
    }

    // ---- helpers -----------------------------------------------------------

    private async Task RequireWorkspaceManagerAsync(Guid workspaceId, Guid userId, CancellationToken ct)
    {
        // 404 when the workspace is gone/not visible to the caller; 403 for a plain Member.
        var exists = await _db.Workspaces.AsNoTracking().AnyAsync(w => w.Id == workspaceId, ct);
        if (!exists)
        {
            throw new NotFoundException("Workspace not found.");
        }

        await _access.RequireManagerAsync(workspaceId, userId, ct);
    }

    /// <summary>Defensive AI output parse: code fence tolerated, failures bubble as JsonException.</summary>
    private static AiObserverOutput ParseOutput(string content)
    {
        var json = StripCodeFence(content);

        return JsonSerializer.Deserialize<AiObserverOutput>(json, Json)
               ?? throw new JsonException("AI trả về JSON rỗng (null).");
    }

    private static string StripCodeFence(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewline < 0 || lastFence <= firstNewline)
        {
            return trimmed;
        }

        return trimmed[(firstNewline + 1)..lastFence].Trim();
    }

    private static List<ObserverFinding> ReadFindings(string? summary)
    {
        if (AiActionService.ParseJson(summary) is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("findings", out var findings)
            || findings.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ObserverFinding>>(findings.GetRawText(), Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ObserverRunResponse ToRunResponse(AiObserverRun run)
    {
        var summary = AiActionService.ParseJson(run.Summary);

        return new ObserverRunResponse(
            run.Id,
            run.WorkspaceId,
            run.Status.ToString(),
            run.StartedAt,
            run.FinishedAt,
            ReadInt(summary, "signalsDetected"),
            ReadInt(summary, "findingsWritten"),
            ReadInt(summary, "notificationsCreated"),
            ReadBool(summary, "aiCalled"))
        {
            // Giai đoạn 14.7: projected from the run summary. Every one of these tolerates absence, so a run
            // recorded before this phase simply reads false/null instead of failing the whole list.
            UsedDetectorFallback = ReadBool(summary, "usedDetectorFallback"),
            DetailError = ReadString(summary, "detailError"),
            JevUsed = ReadBool(summary, "jevUsed"),
            JevSeverity = ReadString(summary, "jevSeverity"),
            JevConfidence = ReadDouble(summary, "jevConfidence"),
            JevCostUsd = ReadDouble(summary, "jevCostUsd"),
            JevInputTokens = ReadNullableInt(summary, "jevInputTokens"),
        };
    }

    private static int ReadInt(JsonElement? element, string propertyName)
        => element is { ValueKind: JsonValueKind.Object } root
           && root.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var result)
            ? result
            : 0;

    /// <summary>Nullable int read: absent/non-numeric ⇒ <c>null</c> (never <c>0</c>, which would be a real value).</summary>
    private static int? ReadNullableInt(JsonElement? element, string propertyName)
        => element is { ValueKind: JsonValueKind.Object } root
           && root.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var result)
            ? result
            : null;

    /// <summary>Nullable double read; absent/non-numeric ⇒ <c>null</c>.</summary>
    private static double? ReadDouble(JsonElement? element, string propertyName)
        => element is { ValueKind: JsonValueKind.Object } root
           && root.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetDouble(out var result)
            ? result
            : null;

    /// <summary>Nullable string read; absent/non-string/blank ⇒ <c>null</c>.</summary>
    private static string? ReadString(JsonElement? element, string propertyName)
        => element is { ValueKind: JsonValueKind.Object } root
           && root.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement? element, string propertyName)
        => element is { ValueKind: JsonValueKind.Object } root
           && root.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.True;

    private static string Cap(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static ObserverScanOutcome Skipped(Guid runId, Guid workspaceId, string reason)
        => new(runId, workspaceId, ObserverRunStatus.Skipped.ToString(), 0, 0, 0, false, reason);

    /// <summary>
    /// Session-level PostgreSQL advisory lock, held on the context's own connection for the whole
    /// run. A transaction is deliberately NOT opened here: the lock must outlive the AI call
    /// without pinning a transaction open (Phase 5 §4.5 step 2).
    /// </summary>
    private async Task<bool> TryAcquireAdvisoryLockAsync(CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = AdvisoryLockKey;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(ct);
        return result is true;
    }

    private async Task ReleaseAdvisoryLockAsync()
    {
        try
        {
            var connection = _db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                return;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock(@key)";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "key";
            parameter.Value = AdvisoryLockKey;
            command.Parameters.Add(parameter);

            await command.ExecuteScalarAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The lock is released automatically when the connection closes; never fail the run.
            _logger.LogWarning(ex, "Observer advisory lock release failed.");
        }
    }

    private sealed record WorkspaceTarget(Guid Id, DateTimeOffset LastActivity);
}
