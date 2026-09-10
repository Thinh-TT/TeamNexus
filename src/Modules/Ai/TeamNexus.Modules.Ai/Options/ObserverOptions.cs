using TeamNexus.Modules.Ai.Services;

namespace TeamNexus.Modules.Ai.Options;

/// <summary>
/// AI Observer configuration (section "Observer" in appsettings / env vars), following the
/// <c>DeepSeekOptions</c> pattern: a plain POCO bound from configuration with no hard validation
/// at startup, so a missing section simply yields the documented defaults.
/// <para>
/// Phase 5 §3 owns the threshold half of this type (consumed by
/// <see cref="ObserverSignalDetector"/>); the scheduling/cost/notification keys are declared here
/// too so §4 only has to register and consume them, never redefine them.
/// </para>
/// </summary>
public sealed class ObserverOptions
{
    public const string SectionName = "Observer";

    // ---- scheduling (consumed by ObserverBackgroundService, §4.6) ----------------

    /// <summary>Master switch. <c>false</c> stops the periodic timer; a manual scan still runs (§4.5).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Scan period in minutes (tech docs §2.3 suggests 15–30). Clamped to 1..1440.</summary>
    public int IntervalMinutes { get; set; } = 30;

    /// <summary>Grace period after startup so the first scan does not compete with cold start.</summary>
    public int StartupDelaySeconds { get; set; } = 60;

    // ---- lookback window (used by ObserverService when loading data, §4.5) -------

    /// <summary>Minimum lookback used to decide whether a workspace had any activity.</summary>
    public int LookbackHours { get; set; } = 24;

    /// <summary>Upper bound for the lookback window (e.g. after the host slept for days).</summary>
    public int MaxLookbackDays { get; set; } = 7;

    /// <summary>Cost control: how many workspaces one run may scan.</summary>
    public int MaxWorkspacesPerRun { get; set; } = 10;

    // ---- signal thresholds (Phase 5 §3 — consumed by ObserverSignalDetector) -----

    /// <summary>An overdue task beyond this many days is reported as Critical (vs High).</summary>
    public int CriticalOverdueDays { get; set; } = 3;

    /// <summary>This many overdue open tasks already counts as an overload signal.</summary>
    public int OverloadOverdueMin { get; set; } = 2;

    /// <summary>A task untouched for this many days counts as stalled.</summary>
    public int StalledDays { get; set; } = 7;

    /// <summary>This many open tasks for one assignee counts as an overload signal.</summary>
    public int OverloadMinOpenTasks { get; set; } = 5;

    /// <summary>Turns the optional Bottleneck signal on/off.</summary>
    public bool BottleneckDetectionEnabled { get; set; } = true;

    /// <summary>Open tasks in one non-done column for it to look like a bottleneck.</summary>
    public int BottleneckMinTasks { get; set; } = 5;

    /// <summary>Stalled tasks in that column required in addition to <see cref="BottleneckMinTasks"/>.</summary>
    public int BottleneckStalledMinTasks { get; set; } = 2;

    /// <summary>Cap on signals returned per workspace (excess is reported as truncated).</summary>
    public int MaxSignalsPerWorkspace { get; set; } = 20;

    /// <summary>Cap on evidence ids (tasks/users) carried by one signal.</summary>
    public int MaxEvidenceIdsPerSignal { get; set; } = 10;

    // ---- AI cost control (consumed by ObserverService, §4.5) ---------------------

    /// <summary>Hard cap on the summarized prompt size — "summarize before sending" (tech docs §2.3).</summary>
    public int MaxPromptCharacters { get; set; } = 12000;

    /// <summary>Output token cap for the Observer call (lower than Smart Setup on purpose).</summary>
    public int MaxOutputTokens { get; set; } = 1500;

    /// <summary>Zero: the Observer interprets data, it must not be creative.</summary>
    public double Temperature { get; set; }

    /// <summary>How much of a task title is kept in the summarized payload.</summary>
    public int MaxTitleExcerptLength { get; set; } = 80;

    // ---- notifications (consumed by NotificationService, §4.4) -------------------

    public int MaxNotificationsPerRun { get; set; } = 50;

    public int MaxManagersPerWorkspace { get; set; } = 10;

    /// <summary>Window in which an identical (workspace, type, entity) alert is not repeated.</summary>
    public int DeduplicationWindowHours { get; set; } = 24;

    /// <summary>Findings below this severity are dropped before a notification is created.</summary>
    public string MinSeverityToNotify { get; set; } = ObserverSeverity.Medium;

    // ---- retention (consumed by ObserverService, §4.5 / DB design §7) ------------

    /// <summary>Activity-log retention; older rows are pruned at the end of a successful run.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Cap on rows deleted per prune pass.</summary>
    public int RetentionDeleteBatchSize { get; set; } = 5000;

    // ---- derived ----------------------------------------------------------------

    /// <summary>Scan period, clamped to a sane range (1 minute .. 1 day).</summary>
    public TimeSpan Interval => TimeSpan.FromMinutes(Math.Clamp(IntervalMinutes, 1, 24 * 60));

    /// <summary>
    /// Lookback window, clamped between <see cref="LookbackHours"/> and
    /// <see cref="MaxLookbackDays"/> so a long host sleep cannot select an unbounded range.
    /// </summary>
    public TimeSpan LookbackWindow
    {
        get
        {
            var hours = Math.Clamp(LookbackHours, 1, Math.Max(1, MaxLookbackDays) * 24);
            return TimeSpan.FromHours(hours);
        }
    }

    /// <summary>
    /// Snapshot of the detection thresholds with defensive clamping, so a misconfigured value
    /// (0/negative) can never make every task a signal or divide by zero.
    /// </summary>
    public ObserverThresholds ToThresholds() => new(
        CriticalOverdueDays: Math.Max(1, CriticalOverdueDays),
        StalledDays: Math.Max(1, StalledDays),
        OverloadMinOpenTasks: Math.Max(1, OverloadMinOpenTasks),
        OverloadOverdueMin: Math.Max(1, OverloadOverdueMin),
        BottleneckDetectionEnabled: BottleneckDetectionEnabled,
        BottleneckMinTasks: Math.Max(1, BottleneckMinTasks),
        BottleneckStalledMinTasks: Math.Max(1, BottleneckStalledMinTasks),
        MaxSignalsPerWorkspace: Math.Max(1, MaxSignalsPerWorkspace),
        MaxEvidenceIdsPerSignal: Math.Max(1, MaxEvidenceIdsPerSignal));
}
