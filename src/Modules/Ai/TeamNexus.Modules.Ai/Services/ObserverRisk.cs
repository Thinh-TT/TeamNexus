using TeamNexus.Shared.Risk;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// One open task that is running out of time with nobody on it (Phase 14 §3.1). Kept separate from
/// <see cref="ObserverSignal"/> so the boundary cases (the 20% edge, the window floor, the 48 h stale
/// gate) can be asserted against this small shape instead of against a whole signal set.
/// </summary>
/// <param name="TaskId">Identity, used for the deterministic tie-break of the evidence list.</param>
/// <param name="MinutesRemaining">
/// Whole minutes until the deadline, always <c>&gt; 0</c> (an already-overdue task belongs to
/// <c>OverdueTask</c> and is never reported here). Used for ordering and for the Vietnamese summary,
/// both of which need a value finer than whole days.
/// </param>
/// <param name="WindowDays">Length of the task's window in whole days (the left edge of the rule).</param>
/// <param name="Severity">Observer severity for this candidate (see <see cref="ObserverRisk.SeverityFor"/>).</param>
/// <param name="AssigneeId">
/// Who is supposed to be on it, when the task is assigned at all. Carried so the signal can name the
/// people involved in the same <c>userIds</c> evidence field the four original signals use — the
/// notification layer and the UI both read it.
/// </param>
public sealed record AiRiskCandidate(
    Guid TaskId,
    int MinutesRemaining,
    int WindowDays,
    string Severity,
    Guid? AssigneeId);

/// <summary>
/// The AI-side adapter over the shared <see cref="DeadlineRiskRules"/> (Phase 14 §3.1).
/// <para>
/// <b>Why the rule lives in Shared and this class is thin:</b> the same "at risk" definition drives the
/// Observer alert here and the <c>AtRiskTasks</c> term of the workspace health score in the Board
/// module. Two implementations of it is exactly how "2 thẻ sắp hết hạn" on the dashboard and the
/// Observer's alert list would end up describing different tasks. Board may not reference Ai and vice
/// versa, so the definition sits in <c>TeamNexus.Shared.Risk</c> and both sides call it.
/// </para>
/// <para>
/// Pure and clock-free: <c>now</c> arrives inside the snapshot, so a test can pin the exact instant.
/// </para>
/// </summary>
public static class ObserverRisk
{
    /// <summary>Upper bound on the "days remaining" used in the Vietnamese summary (keeps text short).</summary>
    public const int MaxRemainingDaysInSummary = 30;

    /// <summary>
    /// Every open task that qualifies as an at-risk deadline, ordered <b>most urgent first</b> and then
    /// by <see cref="Guid"/> so the result never depends on load order.
    /// <para>
    /// Callers pass the <b>open</b> tasks only (the detector drops done-column tasks once for every
    /// detector) and a <see cref="ObserverThresholds"/> that has already been clamped.
    /// </para>
    /// </summary>
    public static IReadOnlyList<AiRiskCandidate> Detect(
        IReadOnlyList<ObserverTaskSnapshot> open,
        DateTimeOffset now,
        ObserverThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(thresholds);

        if (!thresholds.AtRiskDeadlineEnabled)
        {
            return [];
        }

        var candidates = new List<AiRiskCandidate>();

        foreach (var task in open)
        {
            var item = new ProjectWorkItem(
                task.TaskId,
                task.CreatedAt,
                task.DueDate,
                ObserverSignalDetector.ActivityAt(task));

            var risk = DeadlineRiskRules.Classify(
                item,
                now,
                thresholds.AtRiskDeadlineRemainingRatio,
                thresholds.AtRiskDeadlineStaleHours,
                thresholds.AtRiskDeadlineMinWindowDays);

            if (risk == DeadlineRisk.None)
            {
                continue;
            }

            // Classify has already excluded `dueDate == null` and `remaining <= 0`, so the subtraction
            // is safe and always positive here.
            var remaining = task.DueDate!.Value - now;

            candidates.Add(new AiRiskCandidate(
                task.TaskId,
                // Rounded UP: 90 minutes remaining must read as 2 hours, never as 1, so the summary can
                // not understate a deadline that is closer than it says.
                (int)Math.Ceiling(remaining.TotalMinutes),
                (int)Math.Floor((task.DueDate!.Value - task.CreatedAt).TotalDays),
                DeadlineRiskRules.SeverityFor(risk)!,
                task.AssigneeId));
        }

        return candidates
            .OrderBy(c => c.MinutesRemaining)
            .ThenBy(c => c.TaskId)
            .ToList();
    }

    /// <summary>
    /// Vietnamese explanation for the alert ("còn 6 giờ", "còn 2 ngày"). Uses hours under two days so
    /// an urgent task is never rounded into a comfortable-looking "còn 1 ngày".
    /// </summary>
    public static string DescribeRemaining(int minutesRemaining)
    {
        var clamped = Math.Max(0, minutesRemaining);

        if (clamped < 48 * 60)
        {
            var hours = (int)Math.Ceiling(clamped / 60.0);
            return $"còn {hours} giờ";
        }

        var days = Math.Min(MaxRemainingDaysInSummary, (int)Math.Floor(clamped / (60.0 * 24)));
        return $"còn {days} ngày";
    }

    /// <summary>Severity for a candidate, via the shared rule (kept public for direct assertions).</summary>
    public static string SeverityFor(TimeSpan remaining) => DeadlineRiskRules.SeverityFor(
        remaining <= TimeSpan.FromHours(DeadlineRiskRules.CriticalHours)
            ? DeadlineRisk.Critical
            : remaining <= TimeSpan.FromHours(DeadlineRiskRules.UrgentHours)
                ? DeadlineRisk.Urgent
                : DeadlineRisk.AtRisk)!;

    /// <summary>
    /// Builds the single <c>AtRiskDeadline</c> signal for a set of candidates: one alert per workspace
    /// (not one per task) so that N at-risk tasks cost one notification, not N — the same choice the
    /// four original detectors make. Returns <c>null</c> when nothing qualifies.
    /// </summary>
    public static ObserverSignal? BuildSignal(
        IReadOnlyList<AiRiskCandidate> candidates,
        int maxEvidenceIds)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (candidates.Count == 0)
        {
            return null;
        }

        var ordered = candidates
            .OrderBy(c => c.MinutesRemaining)
            .ThenBy(c => c.TaskId)
            .ToList();

        var mostUrgent = ordered[0];

        // The signal's severity is the WORST candidate's: a workspace with one task due in six hours
        // has a critical situation even if the other nine are due next week.
        var severity = ordered
            .OrderByDescending(c => NotificationSeverities.Rank(c.Severity))
            .Select(c => c.Severity)
            .First();

        // Magnitude for deterministic ordering: whole days left, never negative (see the shared rule).
        var weight = Math.Clamp((int)Math.Floor(mostUrgent.MinutesRemaining / (60.0 * 24)), 0, 30);

        var summary = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0} task(s) sắp hết hạn nhưng không có cập nhật trong 48h (gấp nhất {1}).",
            ordered.Count,
            DescribeRemaining(mostUrgent.MinutesRemaining));
        var assignees = ordered
            .Where(c => c.AssigneeId.HasValue)
            .Select(c => c.AssigneeId!.Value)
            .Distinct()
            .OrderBy(id => id)
            .Take(Math.Max(1, maxEvidenceIds))
            .ToList();

        return new ObserverSignal(
            ObserverSignalDetector.AtRiskDeadline,
            severity,
            weight,
            summary,
            ordered.Take(Math.Max(1, maxEvidenceIds)).Select(c => c.TaskId).ToList(),
            assignees);
    }
}
