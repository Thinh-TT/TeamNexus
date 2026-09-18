namespace TeamNexus.Shared.Risk;

/// <summary>
/// One task as the <b>risk</b> rules see it (Phase 14 §3.1). Deliberately a <b>plain</b> record with
/// no EF Core, no HTTP and no clock: the deadline-risk rule is needed by <b>two</b> modules —
/// <c>ObserverSignalDetector</c> (module Ai) produces the <c>AtRiskDeadline</c> alert, and
/// <c>ProjectHealth</c> (module Board) counts <c>AtRiskTasks</c> for the "Sức khỏe dự án" gauge.
/// <para>
/// <b>Why this type lives in Shared and not in one module.</b> The modular monolith keeps a strict
/// one-way dependency <c>Ai → Board</c> (Phase 13 §9): Board may not reference Ai, and Ai may not be
/// referenced by Board. If the rule lived in either module the other one would have to
/// <i>re-implement</i> it — and two implementations of "at risk" is exactly how the gauge and the
/// alert start quoting different numbers. Putting the rule in Shared (which both modules already
/// reference) keeps <b>one</b> definition and no module cycle.
/// </para>
/// </summary>
/// <param name="TaskId">Task identity (used for deterministic ordering of evidence ids).</param>
/// <param name="CreatedAt">When the task was created — the left edge of its window.</param>
/// <param name="DueDate">Due date, or <c>null</c> when the task has no deadline at all.</param>
/// <param name="LastActivityAt">
/// The last moment the task saw any movement. Callers must supply
/// <c>max(updated_at, last_comment_at, created_at)</c> — the exact rule of
/// <c>ObserverSignalDetector.ActivityAt</c> — because a task that is being discussed is <b>not</b>
/// stalled even if its row was never edited.
/// </param>
public sealed record ProjectWorkItem(
    Guid TaskId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DueDate,
    DateTimeOffset LastActivityAt);

/// <summary>
/// The deadline-risk band of one task (Phase 14 §3.1, decision D7 of the phase plan).
/// Ordered by how urgent the situation is — a higher value is worse.
/// </summary>
public enum DeadlineRisk
{
    /// <summary>No due date, already past due, or simply not close enough to the deadline to matter.</summary>
    None = 0,

    /// <summary>Less than 20% of the window is left and nobody touched the task for 48 h.</summary>
    AtRisk = 1,

    /// <summary>Same as <see cref="AtRisk"/> with 72 h or less remaining.</summary>
    Urgent = 2,

    /// <summary>Same as <see cref="AtRisk"/> with 24 h or less remaining.</summary>
    Critical = 3,
}

/// <summary>
/// Which tasks the system considers <b>about to blow a deadline without anyone noticing</b>, and how
/// urgently (Phase 14 §3.1 / decision D7). Pure, deterministic and clock-free: <c>now</c> is a
/// parameter, so every boundary can be asserted exactly in a unit test.
/// <para>
/// <b>The rule (roadmap wording):</b> "task chưa done, còn &lt; 20% thời gian nhưng 0 update trong 48h".
/// Three guards refine it, each one earning its place:
/// </para>
/// <list type="number">
///   <item><b>Window floor</b> (<c>MinWindowDays</c>, default 2). Without it a task created at 10:00
///   and due at 22:00 has an 12-hour window, so "less than 20% left" fires 2 h 24 before the
///   deadline — a false alarm on <i>every</i> short-lived task. Only tasks with a window of at least
///   two days can be "silently running out of time".</item>
///   <item><b>Already overdue ⇒ not ours.</b> When <c>remaining &lt;= 0</c> the task belongs to
///   <c>OverdueTask</c>; reporting it twice would double the alerts for the same problem.</item>
///   <item><b>Movement resets the clock.</b> A comment after the last edit counts as activity
///   (<see cref="ProjectWorkItem.LastActivityAt"/>), so a task under discussion is never called
///   abandoned.</item>
/// </list>
/// <para>
/// <b>Boundary of the 20% edge is strict (&lt;, not ≤):</b> at exactly 20% of the window remaining
/// the task is <b>not</b> yet at risk, so a test can pin the edge from both sides.
/// </para>
/// </summary>
public static class DeadlineRiskRules
{
    /// <summary>Task is at risk once less than this share of its window is left.</summary>
    public const double DefaultRemainingRatio = 0.20;

    /// <summary>How long a task may stay untouched before "running out of time" becomes a concern.</summary>
    public const int DefaultStaleHours = 48;

    /// <summary>Minimum window (due date − creation) for a task to be eligible at all.</summary>
    public const int DefaultMinWindowDays = 2;

    /// <summary>72 h or less remaining, and still untouched ⇒ <see cref="DeadlineRisk.Urgent"/>.</summary>
    public const int UrgentHours = 72;

    /// <summary>24 h or less remaining, and still untouched ⇒ <see cref="DeadlineRisk.Critical"/>.</summary>
    public const int CriticalHours = 24;

    /// <summary>
    /// <c>true</c> when the item is an open task running out of time with nobody on it. Callers must
    /// have already excluded done-column tasks.
    /// </summary>
    public static bool IsAtRiskDeadline(
        ProjectWorkItem item,
        DateTimeOffset now,
        double remainingRatio = DefaultRemainingRatio,
        int staleHours = DefaultStaleHours,
        int minWindowDays = DefaultMinWindowDays)
        => Classify(item, now, remainingRatio, staleHours, minWindowDays) != DeadlineRisk.None;

    /// <summary>Full classification for one task (one call, so both gates can never drift apart).</summary>
    public static DeadlineRisk Classify(
        ProjectWorkItem item,
        DateTimeOffset now,
        double remainingRatio = DefaultRemainingRatio,
        int staleHours = DefaultStaleHours,
        int minWindowDays = DefaultMinWindowDays)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.DueDate is not { } due)
        {
            return DeadlineRisk.None;
        }

        var window = due - item.CreatedAt;
        if (window < TimeSpan.FromDays(Math.Max(1, minWindowDays)))
        {
            return DeadlineRisk.None;
        }

        var remaining = due - now;

        // Already past due ⇒ OverdueTask's business, never reported a second time here.
        if (remaining <= TimeSpan.Zero)
        {
            return DeadlineRisk.None;
        }

        // Strictly less than the share: at exactly the ratio the task is not (yet) at risk.
        if (remaining.TotalSeconds >= Math.Clamp(remainingRatio, 0.01, 1.0) * window.TotalSeconds)
        {
            return DeadlineRisk.None;
        }

        if (now - item.LastActivityAt <= TimeSpan.FromHours(Math.Max(1, staleHours)))
        {
            return DeadlineRisk.None;
        }

        return remaining <= TimeSpan.FromHours(CriticalHours)
            ? DeadlineRisk.Critical
            : remaining <= TimeSpan.FromHours(UrgentHours)
                ? DeadlineRisk.Urgent
                : DeadlineRisk.AtRisk;
    }

    /// <summary>
    /// Whole days left, rounded down and clamped to <c>[0, 30]</c>. Used as the signal's
    /// <c>Weight</c>: it must never be negative (a negative weight would sort a fresh risk below a
    /// solved one) and must not overflow for a far-future deadline.
    /// </summary>
    public static int RemainingDaysWeight(ProjectWorkItem item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.DueDate is not { } due)
        {
            return 0;
        }

        var days = (int)Math.Floor((due - now).TotalDays);
        return Math.Clamp(days, 0, 30);
    }

    /// <summary>Observer severity for a classified risk. <see cref="DeadlineRisk.None"/> ⇒ <c>null</c>.</summary>
    public static string? SeverityFor(DeadlineRisk risk) => risk switch
    {
        DeadlineRisk.Critical => "Critical",
        DeadlineRisk.Urgent => "High",
        DeadlineRisk.AtRisk => "Medium",
        _ => null,
    };

    /// <summary>Vietnamese label used in signal summaries and in the health gauge's reason list.</summary>
    public static string Describe(DeadlineRisk risk) => risk switch
    {
        DeadlineRisk.Critical => "còn ít hơn 1 ngày",
        DeadlineRisk.Urgent => "còn ít hơn 3 ngày",
        DeadlineRisk.AtRisk => "sắp hết thời gian",
        _ => "đúng hạn",
    };
}
