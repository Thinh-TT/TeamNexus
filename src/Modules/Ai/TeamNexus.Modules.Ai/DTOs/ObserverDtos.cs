using System.Text.Json;
using TeamNexus.Modules.Ai.Services;

namespace TeamNexus.Modules.Ai.DTOs;

/// <summary>
/// Raw result of one <c>IObserverService.ScanAsync</c> call (Phase 5 §4.5). Internal to the
/// module: the HTTP layer maps it to <see cref="ObserverScanResponse"/> and drops the diagnostics.
/// <see cref="SkippedReason"/> explains a non-error skip (<c>Disabled</c>/<c>AlreadyRunning</c>/
/// <c>NoWorkspaces</c>/<c>NoSignals</c>) while <see cref="Error"/> carries the failure message when
/// the run ended <c>Failed</c> (the endpoint turns that into a 502).
/// <para>
/// <b>Giai đoạn 14.7 Track A / ISSUE-01:</b> <see cref="DetailError"/> is a <i>softer</i> signal than
/// <see cref="Error"/>. A run whose System 2 (prose) step failed but whose detector output and Jev verdict
/// survived is <c>Completed</c> — it really did produce alerts — and reports the prose failure here instead
/// of pretending the whole scan broke. Only <see cref="Error"/> means "nothing usable came out of this run".
/// </para>
/// </summary>
public sealed record ObserverScanOutcome(
    Guid RunId,
    Guid WorkspaceId,
    string Status,
    int SignalsDetected,
    int FindingsWritten,
    int NotificationsCreated,
    bool AiCalled,
    string? SkippedReason,
    string? Error = null,
    string? DetailError = null,
    bool UsedDetectorFallback = false);

/// <summary>Run history row (Phase 5 §5.3). Counters are projected from the run's jsonb summary.</summary>
public sealed record ObserverRunResponse(
    Guid Id,
    Guid WorkspaceId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int SignalsDetected,
    int FindingsWritten,
    int NotificationsCreated,
    bool AiCalled)
{
    /// <summary>
    /// Giai đoạn 14.7 Track A / ISSUE-01 — true when the run produced its alerts from the detector's own
    /// signals because the System 2 explanation step was unavailable. Projected from the run summary
    /// (<c>usedDetectorFallback</c>) so an older run without the key simply reads <c>false</c>.
    /// </summary>
    public bool UsedDetectorFallback { get; init; }

    /// <summary>The prose-step failure message, when there was one. Null for a fully healthy run.</summary>
    public string? DetailError { get; init; }

    /// <summary>
    /// Giai đoạn 14.7 §A5 — the System One evidence, projected from the summary so the UI can show what Jev
    /// actually decided and what it cost. All null/false on a run that never reached the Jev path.
    /// </summary>
    public bool JevUsed { get; init; }

    public string? JevSeverity { get; init; }

    public double? JevConfidence { get; init; }

    public double? JevCostUsd { get; init; }

    public int? JevInputTokens { get; init; }
}

/// <summary>
/// One validated finding inside a run detail (Phase 5 §5.1).
/// <para>
/// Shape-identical to <see cref="ObserverFinding"/>, which is the domain type the validator and the
/// notification writer use. They are kept as two types so the HTTP contract can evolve without
/// touching the domain record; convert with <see cref="From"/>.
/// </para>
/// </summary>
public sealed record ObserverRunFinding(
    string Type,
    string Severity,
    string Title,
    string Message,
    IReadOnlyList<Guid> TaskIds,
    IReadOnlyList<Guid> UserIds)
{
    public static ObserverRunFinding From(ObserverFinding finding)
        => new(finding.Type, finding.Severity, finding.Title, finding.Message, finding.TaskIds, finding.UserIds);
}

/// <summary>Run detail: the raw summary plus the findings it produced (Phase 5 §5.3).</summary>
public sealed record ObserverRunDetailResponse(
    Guid Id,
    Guid WorkspaceId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    JsonElement? Summary,
    IReadOnlyList<ObserverRunFinding> Findings);

/// <summary>Response of the manual scan endpoint (Phase 5 §5.3).</summary>
/// <param name="DetailError">
/// Giai đoạn 14.7 Track A / ISSUE-01 — set when the run SUCCEEDED but its System 2 explanation step did not.
/// The status stays <c>Completed</c> and alerts were still written; the UI shows this as a warning rather
/// than as a failure, which is exactly the distinction the issue was about.
/// </param>
/// <param name="UsedDetectorFallback">
/// True when the alerts came from the detector's own signals because the model was unavailable. Lets the UI
/// say "cảnh báo từ bộ phát hiện" instead of implying the model wrote the summary.
/// </param>
public sealed record ObserverScanResponse(
    Guid RunId,
    Guid WorkspaceId,
    string Status,
    int SignalsDetected,
    int FindingsWritten,
    int NotificationsCreated,
    bool AiCalled,
    string? DetailError = null,
    bool UsedDetectorFallback = false)
{
    public static ObserverScanResponse From(ObserverScanOutcome outcome)
        => new(outcome.RunId, outcome.WorkspaceId, outcome.Status, outcome.SignalsDetected,
            outcome.FindingsWritten, outcome.NotificationsCreated, outcome.AiCalled,
            outcome.DetailError, outcome.UsedDetectorFallback);
}
