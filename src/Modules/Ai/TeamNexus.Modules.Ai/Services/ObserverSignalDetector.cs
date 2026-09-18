using System.Globalization;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Shared.Risk;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// One open or done task as the Observer sees it at scan time. All time-derived fields are
/// supplied by the caller (no <c>DateTime.Now</c> here) so detection is pure and testable.
/// </summary>
public sealed record ObserverTaskSnapshot(
    Guid TaskId,
    Guid BoardId,
    string BoardName,
    Guid ColumnId,
    string ColumnName,
    string Title,
    Guid? AssigneeId,
    string? AssigneeName,
    DateTimeOffset? DueDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsDoneColumn,
    int CommentCount,
    DateTimeOffset? LastCommentAt);

/// <summary>One board column (used to know which lanes count as "done" and to name them).</summary>
public sealed record ObserverColumnSnapshot(Guid ColumnId, Guid BoardId, string Name, bool IsDone);

/// <summary>
/// Everything one detection pass needs for a single workspace. Later scans are stateless:
/// the snapshot carries the clock (<see cref="Now"/>) instead of the detector reading it.
/// </summary>
public sealed record ObserverWorkspaceSnapshot(
    Guid WorkspaceId,
    IReadOnlyList<ObserverTaskSnapshot> Tasks,
    IReadOnlyList<ObserverColumnSnapshot> Columns,
    DateTimeOffset Now);

/// <summary>
/// One detected problem, ready to be summarized for the AI and later turned into notifications.
/// <para>
/// <see cref="Weight"/> is the ranking magnitude behind the signal (days overdue, days stalled,
/// task count) — it exists so ordering is deterministic and explainable. Evidence ids are
/// already sorted and capped (see <c>ObserverThresholds.MaxEvidenceIdsPerSignal</c>); callers may
/// intersect them with AI-proposed ids but must never widen them (Phase 5 §4.2).
/// </para>
/// </summary>
public sealed record ObserverSignal(
    string Type,
    string Severity,
    int Weight,
    string Summary,
    IReadOnlyList<Guid> TaskIds,
    IReadOnlyList<Guid> UserIds);

/// <summary>Detection result plus how many signals the per-workspace cap dropped (run summary).</summary>
public sealed record ObserverSignalSet(
    IReadOnlyList<ObserverSignal> Signals,
    int TruncatedSignals);

/// <summary>
/// Detection thresholds — already clamped to sane ranges by <see cref="ObserverOptions.ToThresholds"/>.
/// A plain record so verification and Phase 8 unit tests can build one inline without DI/config.
/// <para>
/// The four <c>AtRiskDeadline</c> members were added in Phase 14 §3.1. They are part of this record
/// rather than a second parameter so that <see cref="ObserverSignalDetector.Analyze"/> keeps taking
/// exactly one thresholds object — a second knob bag would let the two drift apart.
/// </para>
/// </summary>
public sealed record ObserverThresholds(
    int CriticalOverdueDays,
    int StalledDays,
    int OverloadMinOpenTasks,
    int OverloadOverdueMin,
    bool BottleneckDetectionEnabled,
    int BottleneckMinTasks,
    int BottleneckStalledMinTasks,
    int MaxSignalsPerWorkspace,
    int MaxEvidenceIdsPerSignal,
    bool AtRiskDeadlineEnabled = true,
    double AtRiskDeadlineRemainingRatio = DeadlineRiskRules.DefaultRemainingRatio,
    int AtRiskDeadlineMinWindowDays = DeadlineRiskRules.DefaultMinWindowDays,
    int AtRiskDeadlineStaleHours = DeadlineRiskRules.DefaultStaleHours);

/// <summary>
/// Pure, deterministic bottleneck/overload/staleness detection for the AI Observer (Phase 5 §3).
/// <para>
/// This class never reads the database, never calls the AI provider and never uses the system
/// clock: <see cref="ObserverService"/> loads the data, builds an <see cref="ObserverWorkspaceSnapshot"/>
/// and passes it here. Keeping the scoring logic pure is what makes it cheap to verify (and to
/// port into xUnit in Phase 8).
/// </para>
/// </summary>
public static class ObserverSignalDetector
{
    /// <summary>Signal type names (also used as <c>notifications.type</c> in §4).</summary>
    public const string OverdueTask = "OverdueTask";

    public const string StalledTask = "StalledTask";

    public const string Overload = "Overload";

    public const string Bottleneck = "Bottleneck";

    /// <summary>
    /// Phase 14 §3.1 — an open task that is running out of time while nobody touches it. The rule
    /// itself lives in the shared <see cref="DeadlineRiskRules"/> (it also feeds the dashboard's
    /// health score); this name is the Observer's own vocabulary entry for it.
    /// </summary>
    public const string AtRiskDeadline = "AtRiskDeadline";

    /// <summary>
    /// Runs every detector over one workspace snapshot and returns the signals ordered by
    /// severity (desc), then weight (desc), then discovery order — truncated to
    /// <see cref="ObserverThresholds.MaxSignalsPerWorkspace"/>.
    /// </summary>
    public static ObserverSignalSet Analyze(ObserverWorkspaceSnapshot snapshot, ObserverThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(thresholds);

        // Done-column tasks are never a problem, so drop them once for every detector.
        var open = snapshot.Tasks.Where(t => !t.IsDoneColumn).ToList();

        var ordered = new List<ObserverSignal>();
        ordered.AddRange(AnalyzeOverdue(open, snapshot.Now, thresholds));
        ordered.AddRange(AnalyzeStalled(open, snapshot.Now, thresholds));

        // Phase 14 §3.1. Deliberately AFTER overdue: a task that is already past due is OverdueTask's
        // business and ObserverRisk excludes it, so the two detectors can never describe one task
        // twice. Placing the deadline detector here also keeps the discovery order of the four
        // original signals byte-identical.
        if (ObserverRisk.BuildSignal(
                ObserverRisk.Detect(open, snapshot.Now, thresholds),
                thresholds.MaxEvidenceIdsPerSignal) is { } atRisk)
        {
            ordered.Add(atRisk);
        }

        ordered.AddRange(AnalyzeOverload(open, snapshot.Now, thresholds));
        ordered.AddRange(AnalyzeBottleneck(open, snapshot.Now, thresholds));

        // Severity first, then magnitude; OrderBy is a stable sort, so ties keep discovery order.
        var sorted = ordered
            .OrderByDescending(s => NotificationSeverities.Rank(s.Severity))
            .ThenByDescending(s => s.Weight)
            .ToList();

        var truncated = Math.Max(0, sorted.Count - thresholds.MaxSignalsPerWorkspace);

        return new ObserverSignalSet(sorted.Take(thresholds.MaxSignalsPerWorkspace).ToList(), truncated);
    }

    // ---- detectors ---------------------------------------------------------

    private static IEnumerable<ObserverSignal> AnalyzeOverdue(
        IReadOnlyList<ObserverTaskSnapshot> open,
        DateTimeOffset now,
        ObserverThresholds thresholds)
    {
        var overdue = open
            .Where(t => t.DueDate.HasValue && t.DueDate.Value < now)
            .Select(t => new { Task = t, Days = Days(t.DueDate!.Value, now) })
            .ToList();

        if (overdue.Count == 0)
        {
            yield break;
        }

        var worst = overdue.Max(x => x.Days);

        // Severe only when the delay is clearly beyond the alert threshold (strict >, per §3.2).
        var severity = worst > 2 * thresholds.CriticalOverdueDays
            ? ObserverSeverity.Critical
            : ObserverSeverity.High;

        var tasks = overdue
            .OrderByDescending(x => x.Days)
            .ThenBy(x => x.Task.TaskId)
            .Select(x => x.Task.TaskId)
            .ToList();

        var summary = string.Format(
            CultureInfo.InvariantCulture,
            "{0} task(s) past due (worst {1} day(s) late).",
            overdue.Count,
            worst);

        yield return new ObserverSignal(
            OverdueTask,
            severity,
            worst,
            summary,
            CapIds(tasks, thresholds.MaxEvidenceIdsPerSignal),
            CapIds(Assignees(overdue.Select(x => x.Task)), thresholds.MaxEvidenceIdsPerSignal));
    }

    private static IEnumerable<ObserverSignal> AnalyzeStalled(
        IReadOnlyList<ObserverTaskSnapshot> open,
        DateTimeOffset now,
        ObserverThresholds thresholds)
    {
        var reference = TimeSpan.FromDays(thresholds.StalledDays);

        var stalled = open
            .Select(t => new { Task = t, Activity = ActivityAt(t) })
            // Strictly older than the threshold: a task idle for exactly StalledDays is not yet
            // reported (Phase 5 §3.2: "max(...) < now − StalledDays").
            .Where(x => now - x.Activity > reference)
            .Select(x => new { x.Task, x.Activity, Days = Days(x.Activity, now) })
            .ToList();

        if (stalled.Count == 0)
        {
            yield break;
        }

        var worst = stalled.Max(x => x.Days);
        var severity = worst >= 2 * thresholds.StalledDays
            ? ObserverSeverity.High
            : ObserverSeverity.Medium;

        var tasks = stalled
            .OrderByDescending(x => x.Days)
            .ThenBy(x => x.Task.TaskId)
            .Select(x => x.Task.TaskId)
            .ToList();

        var summary = string.Format(
            CultureInfo.InvariantCulture,
            "{0} task(s) untouched for at least {1} day(s) (worst {2} day(s)).",
            stalled.Count,
            thresholds.StalledDays,
            worst);

        yield return new ObserverSignal(
            StalledTask,
            severity,
            worst,
            summary,
            CapIds(tasks, thresholds.MaxEvidenceIdsPerSignal),
            CapIds(Assignees(stalled.Select(x => x.Task)), thresholds.MaxEvidenceIdsPerSignal));
    }

    private static IEnumerable<ObserverSignal> AnalyzeOverload(
        IReadOnlyList<ObserverTaskSnapshot> open,
        DateTimeOffset now,
        ObserverThresholds thresholds)
    {
        var groups = open
            .Where(t => t.AssigneeId.HasValue)
            .GroupBy(t => t.AssigneeId!.Value);

        // GroupBy keeps first-appearance order, so the produced signals are deterministic.
        foreach (var group in groups)
        {
            var tasks = group.ToList();
            var openCount = tasks.Count;

            var overdue = tasks
                .Where(t => t.DueDate.HasValue && t.DueDate.Value < now)
                .Select(t => new { Task = t, Days = Days(t.DueDate!.Value, now) })
                .OrderByDescending(x => x.Days)
                .ThenBy(x => x.Task.TaskId)
                .ToList();

            var overdueCount = overdue.Count;

            if (overdueCount < thresholds.OverloadOverdueMin
                && openCount < thresholds.OverloadMinOpenTasks)
            {
                continue;
            }

            var severity = openCount >= 2 * thresholds.OverloadMinOpenTasks
                ? ObserverSeverity.Critical
                : ObserverSeverity.High;

            // Overdue tasks are the strongest evidence, then whatever else is still open.
            var evidence = overdue.Select(x => x.Task.TaskId)
                .Concat(tasks
                    .Except(overdue.Select(x => x.Task))
                    .OrderBy(ActivityAt)
                    .ThenBy(t => t.TaskId)
                    .Select(t => t.TaskId))
                .Distinct()
                .ToList();

            var name = tasks.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.AssigneeName))?.AssigneeName
                       ?? group.Key.ToString();

            var summary = string.Format(
                CultureInfo.InvariantCulture,
                "{0} has {1} open task(s) and {2} past due.",
                name,
                openCount,
                overdueCount);

            yield return new ObserverSignal(
                Overload,
                severity,
                Math.Max(openCount, overdueCount),
                summary,
                CapIds(evidence, thresholds.MaxEvidenceIdsPerSignal),
                [group.Key]);
        }
    }

    private static IEnumerable<ObserverSignal> AnalyzeBottleneck(
        IReadOnlyList<ObserverTaskSnapshot> open,
        DateTimeOffset now,
        ObserverThresholds thresholds)
    {
        if (!thresholds.BottleneckDetectionEnabled)
        {
            yield break;
        }

        var reference = TimeSpan.FromDays(thresholds.StalledDays);

        var groups = open.GroupBy(t => t.ColumnId);

        foreach (var group in groups)
        {
            var tasks = group.ToList();
            var openCount = tasks.Count;

            var stalled = tasks
                .Select(t => new { Task = t, Activity = ActivityAt(t) })
                // Same strict rule as StalledTask: idle longer than StalledDays (Phase 5 §3.2).
                .Where(x => now - x.Activity > reference)
                .Select(x => new { x.Task, Days = Days(x.Activity, now) })
                .OrderByDescending(x => x.Days)
                .ThenBy(x => x.Task.TaskId)
                .ToList();

            if (openCount < thresholds.BottleneckMinTasks
                || stalled.Count < thresholds.BottleneckStalledMinTasks)
            {
                continue;
            }

            var columnName = tasks[0].ColumnName;
            var summary = string.Format(
                CultureInfo.InvariantCulture,
                "Column '{0}' holds {1} open task(s), {2} of them stalled.",
                columnName,
                openCount,
                stalled.Count);

            yield return new ObserverSignal(
                Bottleneck,
                ObserverSeverity.Medium,
                openCount,
                summary,
                CapIds(stalled.Select(x => x.Task.TaskId), thresholds.MaxEvidenceIdsPerSignal),
                CapIds(Assignees(stalled.Select(x => x.Task)), thresholds.MaxEvidenceIdsPerSignal));
        }
    }

    // ---- pure helpers ------------------------------------------------------

    /// <summary>
    /// Last moment a task saw any movement: a comment newer than the last edit also counts, so a
    /// discussion "revives" a task instead of it being reported as stalled forever.
    /// </summary>
    internal static DateTimeOffset ActivityAt(ObserverTaskSnapshot task)
    {
        if (task.LastCommentAt.HasValue && task.LastCommentAt.Value > task.UpdatedAt)
        {
            return task.LastCommentAt.Value;
        }

        return task.UpdatedAt > task.CreatedAt ? task.UpdatedAt : task.CreatedAt;
    }

    private static int Days(DateTimeOffset from, DateTimeOffset to)
        => (int)(to - from).TotalDays;

    /// <summary>Distinct, sorted assignee ids (deterministic regardless of load order).</summary>
    private static List<Guid> Assignees(IEnumerable<ObserverTaskSnapshot> tasks)
        => tasks
            .Where(t => t.AssigneeId.HasValue)
            .Select(t => t.AssigneeId!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

    private static IReadOnlyList<Guid> CapIds(IEnumerable<Guid> ids, int max)
        => ids.Take(max).ToList();
}
