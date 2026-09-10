namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Lifecycle of one AI Observer scan for one workspace (Phase 5 §1 / DB design §3.6).
/// Stored as text + CHECK constraint <c>ck_ai_observer_runs_status</c>.
/// </summary>
public enum ObserverRunStatus
{
    /// <summary>Scan in progress (or crashed mid-run — <c>finished_at</c> stays null).</summary>
    Running,

    /// <summary>Scan finished (with or without findings); the only status that produces notifications.</summary>
    Completed,

    /// <summary>
    /// Nothing was scanned because the Observer is disabled or another run holds the advisory
    /// lock. <b>Not</b> an error and never produces notifications.
    /// </summary>
    Skipped,

    /// <summary>Scan failed (AI provider error, unparsable JSON, DB error). No notifications written.</summary>
    Failed,
}

/// <summary>
/// Audit + dedup record for the AI Observer's periodic background scan: one row per workspace per
/// run (Phase 5 §1 / DB design §3.6). Table: <c>ai_observer_runs</c>.
/// </summary>
public class AiObserverRun : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Workspace this scan covered (one run = one workspace).</summary>
    public Guid WorkspaceId { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Set when the run leaves <see cref="ObserverRunStatus.Running"/>.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    public ObserverRunStatus Status { get; set; } = ObserverRunStatus.Running;

    /// <summary>
    /// jsonb — run summary: signalsDetected, signalsByType, truncatedSignals, findingsWritten,
    /// notificationsCreated, aiCalled, model, promptTokens, completionTokens, durationMs,
    /// skippedReason, error.
    /// </summary>
    public string? Summary { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
