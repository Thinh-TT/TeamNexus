using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using TeamNexus.Modules.Ai.Contracts;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Options;
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
        ILogger<ObserverService> logger)
    {
        _db = db;
        _access = access;
        _aiProvider = aiProvider;
        _notifications = notifications;
        _options = options.Value;
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
        var skippedReason = (string?)null;
        string? model = null;
        int? promptTokens = null;
        int? completionTokens = null;
        var truncatedByPrompt = 0;

        try
        {
            if (signals.Signals.Count == 0)
            {
                // 5) Nothing to interpret ⇒ never spend a token (roadmap requirement).
                skippedReason = "NoSignals";
            }
            else
            {
                // 6–7) Summarize, then one non-retrying completion call.
                var request = ObserverSummarizer.BuildRequest(snapshot, signals, _options);
                truncatedByPrompt = Math.Max(0, signals.Signals.Count - CountSignalsInPayload(request.UserPrompt));

                _logger.LogDebug(
                    "Observer prompt for workspace {WorkspaceId}: {Length} ký tự, {Signals} tín hiệu.",
                    workspaceId,
                    request.UserPrompt.Length,
                    signals.Signals.Count);

                aiCalled = true;
                var completion = await _aiProvider.CompleteAsync(request, ct);
                promptTokens = completion.PromptTokens;
                completionTokens = completion.CompletionTokens;
                model = _providerModel;

                var output = ParseOutput(completion.Content);

                // 8) Anti-hallucination boundary.
                findings = ObserverFindingValidator.Validate(output, signals.Signals, _options).ToList();
            }
        }
        catch (AiProviderException ex)
        {
            error = Cap(ex.Message, 500);
            _logger.LogWarning(ex, "Observer scan failed for workspace {WorkspaceId} (AI provider).", workspaceId);
        }
        catch (JsonException ex)
        {
            error = Cap(ex.Message, 500);
            _logger.LogWarning(ex, "Observer scan failed for workspace {WorkspaceId} (unparsable AI output).", workspaceId);
        }

        var status = error is not null ? ObserverRunStatus.Failed : ObserverRunStatus.Completed;

        // 9) Notify (only on a successful run), then record the run + prune old activity.
        var notificationsCreated = 0;
        if (status == ObserverRunStatus.Completed && findings.Count > 0)
        {
            var context = new ObserverRunContext(runId, model ?? "unspecified", promptTokens, completionTokens);
            notificationsCreated = await _notifications.NotifyManagersAsync(workspaceId, findings, context, ct);
        }

        stopwatch.Stop();

        await PersistRunAsync(
            workspaceId, runId, startedAt, status, signals, signalsByType, findings, notificationsCreated,
            aiCalled, model, promptTokens, completionTokens, truncatedByPrompt, skippedReason, error,
            stopwatch.ElapsedMilliseconds, ct);

        if (status == ObserverRunStatus.Completed)
        {
            await PruneActivityAsync(workspaceId, ct);
        }

        _logger.LogInformation(
            "Observer run {RunId} for workspace {WorkspaceId}: status={Status}, signals={Signals}, "
            + "findings={Findings}, notifications={Notifications}, aiCalled={AiCalled}, durationMs={DurationMs}.",
            runId,
            workspaceId,
            status,
            signals.Signals.Count,
            findings.Count,
            notificationsCreated,
            aiCalled,
            stopwatch.ElapsedMilliseconds);

        return new ObserverScanOutcome(
            runId, workspaceId, status.ToString(), signals.Signals.Count, findings.Count,
            notificationsCreated, aiCalled, skippedReason, error);
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
        CancellationToken ct)
    {
        var summary = JsonSerializer.Serialize(new
        {
            runId,
            signalsDetected = signals.Signals.Count,
            signalsByType,
            truncatedSignals = signals.TruncatedSignals,
            truncatedByPrompt,
            findingsWritten = findings.Count,
            notificationsCreated,
            aiCalled,
            model,
            promptTokens,
            completionTokens,
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
            ReadBool(summary, "aiCalled"));
    }

    private static int ReadInt(JsonElement? element, string propertyName)
        => element is { ValueKind: JsonValueKind.Object } root
           && root.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var result)
            ? result
            : 0;

    private static bool ReadBool(JsonElement? element, string propertyName)
        => element is { ValueKind: JsonValueKind.Object } root
           && root.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.True;

    /// <summary>How many signals survived prompt truncation (for the run summary).</summary>
    private static int CountSignalsInPayload(string userPrompt)
    {
        var markerIndex = userPrompt.IndexOf(ObserverPrompts.AgentMarker, StringComparison.Ordinal);
        var json = markerIndex < 0 ? userPrompt : userPrompt[(markerIndex + ObserverPrompts.AgentMarker.Length)..];

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("signals", out var signals)
                   && signals.ValueKind == JsonValueKind.Array
                ? signals.GetArrayLength()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

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
