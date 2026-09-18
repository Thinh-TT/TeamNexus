using TeamNexus.Shared.Risk;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Everything the health formula needs, as <b>counts</b> — never tasks, never a DbContext. Computing
/// the counts belongs to <see cref="DashboardService"/> (it already loads exactly these rows), so the
/// formula itself stays a pure function that can be verified without a database.
/// </summary>
/// <param name="TotalTasks">All tasks of the workspace's visible boards.</param>
/// <param name="OpenTasks">Tasks that are not done (the denominator of every ratio).</param>
/// <param name="OverdueTasks">Open tasks past their due date (strictly: <c>dueDate &lt; now</c>).</param>
/// <param name="AtRiskTasks">
/// Open tasks that are running out of time with nobody on them — counted with
/// <see cref="DeadlineRiskRules.IsAtRiskDeadline"/>, so this is <b>by construction</b> the same rule
/// the AI Observer reports as <c>AtRiskDeadline</c>. If the two ever disagreed, the dashboard gauge
/// and the alert list would contradict each other.
/// </param>
/// <param name="StalledTasks">Open tasks untouched for more than <c>StalledDays</c>.</param>
/// <param name="OldestOpenTaskAgeDays">Age of the oldest open task, in whole days (0 when there is none).</param>
/// <param name="MaxOpenTasksPerAssignee">Largest number of open tasks held by one assignee.</param>
/// <param name="AssigneeCount">How many distinct assignees hold at least one open task.</param>
public sealed record ProjectHealthInput(
    int TotalTasks,
    int OpenTasks,
    int OverdueTasks,
    int AtRiskTasks,
    int StalledTasks,
    int OldestOpenTaskAgeDays,
    int MaxOpenTasksPerAssignee,
    int AssigneeCount);

/// <summary>Which band a score falls into. Carried as text so the UI never re-derives the thresholds.</summary>
public static class ProjectHealthBands
{
    public const string Good = "Tốt";

    public const string Watch = "Cần chú ý";

    public const string Risky = "Rủi ro";

    public const string Critical = "Nghiêm trọng";
}

/// <summary>
/// The verdict: a 0–100 score, the band it falls into, the <b>five penalty components</b> that
/// produced it, and a short Vietnamese explanation list.
/// <para>
/// The components are the <b>points deducted</b>, not percentages: they add up with
/// <see cref="Score"/> to 100, which is what makes the gauge's tooltip self-explanatory ("72 điểm –
/// trừ 20 vì quá hạn – trừ 6 vì sắp hết hạn").
/// </para>
/// </summary>
public sealed record ProjectHealthResult(
    int Score,
    string Band,
    IReadOnlyDictionary<string, double> Components,
    IReadOnlyList<string> Reasons);

/// <summary>
/// Workspace "sức khỏe dự án" (Phase 14 §3.2, decisions D6/D8 of the phase plan).
/// <para>
/// <b>Why this lives in the Board module and not in <c>ObserverSignalDetector</c>:</b> the roadmap
/// listed it next to the Observer signal, but the gauge is rendered on the workspace dashboard, which
/// is <b>Member+</b>, while the Observer's own endpoints are <b>Manager+</b>. Putting the score in the
/// Ai module would either hide the gauge from ordinary members or force the Observer's authorization to
/// be widened. On top of that, Board may not reference Ai (one-way dependency, Phase 13 §9), so a
/// Board-owned dashboard could never embed an Ai-owned value. The <b>risk rule itself</b> is shared
/// (<see cref="DeadlineRiskRules"/>), which is the part that must not drift; the score is a read model
/// over data the dashboard already loads. This deviation is recorded in
/// <c>Project-Documents/03-roadmap.md</c> and in the Phase 14 report — see decision D6.
/// </para>
/// <para>
/// <b>Deterministic and total:</b> no clock, no I/O, every ratio guarded against a zero denominator,
/// and the result clamped to <c>[0, 100]</c>. The five weights deliberately sum to <b>100</b>
/// (40 + 20 + 10 + 15 + 15), so a workspace with every risk factor saturating really is a 0 — the
/// clamp is a guard against a miscounted input, not a permanent floor that would make the bottom of
/// the scale unreachable.
/// </para>
/// </summary>
public static class ProjectHealth
{
    /// <summary>Penalty when <b>every</b> open task is overdue (the single worst signal there is).</summary>
    public const double OverdueWeight = 40;

    /// <summary>Penalty when every open task is running out of time with nobody on it.</summary>
    public const double AtRiskWeight = 20;

    /// <summary>Penalty when every open task has been untouched past the stall threshold.</summary>
    public const double StalledWeight = 10;

    /// <summary>Penalty for the oldest open task reaching <see cref="AgingReferenceDays"/>.</summary>
    public const double AgingWeight = 15;

    /// <summary>Penalty for one assignee carrying a lopsided share of the open work.</summary>
    public const double LoadWeight = 15;

    /// <summary>An open task this old contributes the full aging penalty.</summary>
    public const int AgingReferenceDays = 30;

    /// <summary>Below this many open tasks for one assignee, load is not a problem at all.</summary>
    public const int LoadFreeTasks = 1;

    /// <summary>Reaching this many open tasks for one assignee contributes the full load penalty.</summary>
    public const int LoadSaturatedTasks = 5;

    /// <summary>Score at or above this is "Tốt".</summary>
    public const int GoodThreshold = 80;

    /// <summary>Score at or above this (and below <see cref="GoodThreshold"/>) is "Cần chú ý".</summary>
    public const int WatchThreshold = 60;

    /// <summary>Score at or above this (and below <see cref="WatchThreshold"/>) is "Rủi ro".</summary>
    public const int RiskyThreshold = 40;

    /// <summary>Stalled definition used to count <see cref="ProjectHealthInput.StalledTasks"/>.</summary>
    public const int DefaultStalledDays = 7;

    /// <summary>
    /// Scores one workspace. <paramref name="thresholds"/> is accepted so the risk half of the count
    /// can be produced with the same knobs the Observer uses; the default matches the phase plan.
    /// </summary>
    public static ProjectHealthResult Compute(
        ProjectHealthInput input,
        ProjectHealthThresholds? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        var effective = thresholds ?? new ProjectHealthThresholds();

        // Every ratio is over OPEN tasks: a workspace whose work is all finished is healthy, no matter
        // how much of it was late. Zero open tasks ⇒ every ratio is 0, never a division by zero.
        var open = Math.Max(0, input.OpenTasks);
        var hasOpenWork = open > 0;

        var overdueRatio = Ratio(input.OverdueTasks, open);
        var atRiskRatio = Ratio(input.AtRiskTasks, open);
        var stalledRatio = Ratio(input.StalledTasks, open);

        var overdue = OverdueWeight * overdueRatio;
        var atRisk = AtRiskWeight * atRiskRatio;
        var stalled = StalledWeight * stalledRatio;

        // Aging and load are gated on there being open work at all: with nothing open, "the oldest
        // open task is 400 days old" or "one person holds 50 open tasks" cannot describe reality, so a
        // contradictory pair of counters must not be able to dent the score. The overdue/atRisk/stalled
        // ratios already collapse to 0 through their zero denominator; these two have no denominator.
        var aging = !hasOpenWork
            ? 0
            : AgingWeight * Clamp01(input.OldestOpenTaskAgeDays / (double)AgingReferenceDays);

        var load = !hasOpenWork || input.AssigneeCount <= 0
            ? 0
            : LoadWeight * Clamp01(
                (input.MaxOpenTasksPerAssignee - LoadFreeTasks)
                / (double)(LoadSaturatedTasks - LoadFreeTasks));

        var components = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["overdue"] = Round(overdue),
            ["atRisk"] = Round(atRisk),
            ["stalled"] = Round(stalled),
            ["aging"] = Round(aging),
            ["load"] = Round(load),
        };

        var score = (int)Math.Clamp(
            Math.Round(100 - overdue - atRisk - stalled - aging - load, MidpointRounding.AwayFromZero),
            0,
            100);

        return new ProjectHealthResult(score, BandFor(score), components, BuildReasons(input, components));
    }

    /// <summary>Band for a score. Exposed so a test can pin the thresholds without recomputing ratios.</summary>
    public static string BandFor(int score)
    {
        if (score >= GoodThreshold)
        {
            return ProjectHealthBands.Good;
        }

        return score >= WatchThreshold
            ? ProjectHealthBands.Watch
            : score >= RiskyThreshold
                ? ProjectHealthBands.Risky
                : ProjectHealthBands.Critical;
    }

    /// <summary>
    /// Vietnamese explanations, only for penalties that are actually non-zero. An empty list is the
    /// normal case for a healthy workspace — the UI shows nothing rather than inventing a reason.
    /// </summary>
    private static IReadOnlyList<string> BuildReasons(
        ProjectHealthInput input,
        IReadOnlyDictionary<string, double> components)
    {
        var reasons = new List<string>();

        if (components["overdue"] > 0)
        {
            reasons.Add($"{input.OverdueTasks} thẻ quá hạn");
        }

        if (components["atRisk"] > 0)
        {
            reasons.Add($"{input.AtRiskTasks} thẻ sắp hết hạn mà không ai cập nhật");
        }

        if (components["stalled"] > 0)
        {
            reasons.Add($"{input.StalledTasks} thẻ đứng yên quá {DefaultStalledDays} ngày");
        }

        if (components["aging"] > 0)
        {
            reasons.Add($"Thẻ mở lâu nhất đã {input.OldestOpenTaskAgeDays} ngày");
        }

        if (components["load"] > 0)
        {
            reasons.Add($"Một người đang giữ {input.MaxOpenTasksPerAssignee} thẻ mở");
        }

        return reasons;
    }

    private static double Ratio(int count, int open)
        => open <= 0 ? 0 : Clamp01(Math.Max(0, count) / (double)open);

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    /// <summary>Two decimals — enough to explain a score, small enough to keep the JSON tidy.</summary>
    private static double Round(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Knobs for the risk half of the count. Defaults are the phase-plan values (D7); the shape mirrors
/// <c>ObserverOptions</c> so a future change can be wired from configuration without touching the formula.
/// </summary>
public sealed record ProjectHealthThresholds(
    double AtRiskRemainingRatio = DeadlineRiskRules.DefaultRemainingRatio,
    int AtRiskStaleHours = DeadlineRiskRules.DefaultStaleHours,
    int AtRiskMinWindowDays = DeadlineRiskRules.DefaultMinWindowDays,
    int StalledDays = ProjectHealth.DefaultStalledDays);
