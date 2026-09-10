using System.Text.Json;
using TeamNexus.Modules.Ai.Services;

namespace TeamNexus.Modules.Ai.DTOs;

/// <summary>
/// Raw result of one <c>IObserverService.ScanAsync</c> call (Phase 5 §4.5). Internal to the
/// module: the HTTP layer maps it to <see cref="ObserverScanResponse"/> and drops the diagnostics.
/// <see cref="SkippedReason"/> explains a non-error skip (<c>Disabled</c>/<c>AlreadyRunning</c>/
/// <c>NoWorkspaces</c>/<c>NoSignals</c>) while <see cref="Error"/> carries the failure message when
/// the run ended <c>Failed</c> (the endpoint turns that into a 502).
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
    string? Error = null);

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
    bool AiCalled);

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
public sealed record ObserverScanResponse(
    Guid RunId,
    Guid WorkspaceId,
    string Status,
    int SignalsDetected,
    int FindingsWritten,
    int NotificationsCreated,
    bool AiCalled)
{
    public static ObserverScanResponse From(ObserverScanOutcome outcome)
        => new(outcome.RunId, outcome.WorkspaceId, outcome.Status, outcome.SignalsDetected,
            outcome.FindingsWritten, outcome.NotificationsCreated, outcome.AiCalled);
}
