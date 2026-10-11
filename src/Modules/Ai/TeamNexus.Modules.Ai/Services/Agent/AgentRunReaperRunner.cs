using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>
/// One reaping pass: closes the agent runs that can never finish (Phase 7 §4.8 D13, extended in
/// Phase 14.5 §Track A / A2.1).
/// <para>
/// <b>Why this is a separate scoped type and not the hosted service itself.</b> Exactly the
/// <c>DailyDigestRunner</c> reasoning: the sweep has to be exercisable by a test with no timer, no startup
/// delay and no waiting for a <c>BackgroundService</c> to wake up. Extracting it also removed a real
/// obstacle — <c>AgentRunReaper</c> is only registered as a hosted service, so a test previously could not
/// resolve it at all. The hosted service now decides <i>when</i>; this decides <i>what</i>.
/// </para>
/// </summary>
public sealed class AgentRunReaperRunner
{
    private readonly TeamNexusDbContext _db;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentRunReaperRunner> _logger;

    public AgentRunReaperRunner(
        TeamNexusDbContext db,
        IOptions<AgentOptions> options,
        ILogger<AgentRunReaperRunner> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Closes both families of unfinishable run and returns how many rows were closed.
    /// <paramref name="now"/> is a parameter rather than a clock read so a test can move time instead of
    /// waiting for it.
    /// </summary>
    public async Task<int> RunOnceAsync(DateTimeOffset? now = null, CancellationToken ct = default)
    {
        var effective = _options.Effective;
        var utcNow = now ?? DateTimeOffset.UtcNow;
        var cutoff = utcNow - _options.OrphanCutoff;

        var closedByAge = await ReapStaleRunsAsync(cutoff, utcNow, ct);
        var closedByDeletion = await ReapRunsOfDeletedTasksAsync(utcNow, ct);

        if (closedByAge > 0)
        {
            _logger.LogWarning(
                "Agent run reaper closed {Count} orphaned run(s) older than {Cutoff} "
                + "(RunTimeoutSeconds={Timeout}s + OrphanRunGraceSeconds={Grace}s).",
                closedByAge,
                cutoff,
                effective.RunTimeoutSeconds,
                effective.OrphanRunGraceSeconds);
        }

        if (closedByDeletion > 0)
        {
            _logger.LogInformation(
                "Agent run reaper closed {Count} run(s) whose task was soft-deleted while they were live.",
                closedByDeletion);
        }

        if (closedByAge == 0 && closedByDeletion == 0)
        {
            _logger.LogInformation("Agent run reaper found no orphaned run to close.");
        }

        return closedByAge + closedByDeletion;
    }

    /// <summary>
    /// The original Phase 7 sweep (D13): runs stuck in <c>Running</c> because the host slept or was
    /// recycled mid-run. Uses the partial index <c>(status) WHERE status = 'Running'</c>.
    /// </summary>
    private async Task<int> ReapStaleRunsAsync(DateTimeOffset cutoff, DateTimeOffset now, CancellationToken ct)
        => await _db.AgentRuns
            .Where(r => r.Status == AgentRunStatus.Running && r.StartedAt < cutoff)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, AgentRunStatus.Failed)
                .SetProperty(r => r.StopReason, AgentStopReason.InternalError)
                .SetProperty(r => r.Error, "Lượt chạy bị bỏ dở (ứng dụng đã ngủ/khởi động lại) — đóng bởi AgentRunReaper.")
                .SetProperty(r => r.FinishedAt, now)
                .SetProperty(r => r.UpdatedAt, now), ct);

    /// <summary>
    /// Phase 14.5 (A2.1): runs that are still live but whose <b>task has been soft-deleted</b>.
    /// <para>
    /// Why the age sweep does not cover this: it only looks at the clock, so a task deleted one minute into
    /// a run leaves that run <c>Running</c> (or <c>AwaitingApproval</c>) for the whole
    /// <c>RunTimeoutSeconds + OrphanRunGraceSeconds</c> window — during which the Kanban card it belongs to
    /// is already hidden, the clarification column may be recycled for another task, and a later assignment
    /// attempt is refused with a 409 that names a run nobody can see.
    /// </para>
    /// <para>
    /// <c>stop_reason = TaskChanged</c> mirrors exactly what a live orchestrator records when it notices the
    /// task moved out from under it — and, like there, <b>no notification</b> is written: deleting a card is
    /// already a deliberate human act, so alerting a Manager about it would be noise.
    /// </para>
    /// </summary>
    private async Task<int> ReapRunsOfDeletedTasksAsync(DateTimeOffset now, CancellationToken ct)
    {
        // The Tasks query filter hides soft-deleted rows, so this set difference IS the "task is gone" test
        // — using it (rather than IgnoreQueryFilters) keeps the rule true if the filter ever changes.
        AgentRunStatus[] liveStatuses =
        [
            AgentRunStatus.Running,
            AgentRunStatus.AwaitingClarification,
            AgentRunStatus.AwaitingApproval,
        ];

        var orphanedRunIds = await _db.AgentRuns
            .AsNoTracking()
            .Where(r => liveStatuses.Contains(r.Status) && !_db.Tasks.Any(t => t.Id == r.TaskId))
            .Select(r => r.Id)
            .ToListAsync(ct);

        if (orphanedRunIds.Count == 0)
        {
            return 0;
        }

        return await _db.AgentRuns
            .Where(r => orphanedRunIds.Contains(r.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, AgentRunStatus.Failed)
                .SetProperty(r => r.StopReason, AgentStopReason.TaskChanged)
                .SetProperty(r => r.Error, "Task của lượt chạy đã bị xoá — đóng bởi AgentRunReaper.")
                .SetProperty(r => r.FinishedAt, now)
                .SetProperty(r => r.UpdatedAt, now), ct);
    }
}
