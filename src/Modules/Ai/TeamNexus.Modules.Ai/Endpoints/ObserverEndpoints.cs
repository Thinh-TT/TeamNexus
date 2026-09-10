using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Persistence.Data.Entities;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Ai.Endpoints;

/// <summary>
/// AI Observer endpoints (Phase 5 §5.3): trigger a scan on demand and inspect past runs.
/// <para>
/// All three routes are Manager/Admin only — the roadmap requirement is that bottleneck/overload
/// alerts are never public to the whole team. Authorization happens inside <c>ObserverService</c>
/// (<c>IWorkspaceAccess.RequireManagerAsync</c> → 403, unknown workspace → 404), matching the Board
/// module's "authenticate at the edge, authorize in the service" pattern.
/// </para>
/// </summary>
public static class ObserverEndpoints
{
    public static IEndpointRouteBuilder MapObserverEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var workspaceGroup = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/observer")
            .WithTags("AiObserver")
            .AddEndpointFilter<DomainExceptionFilter>();

        // Synchronous on purpose: the response carries the run result immediately (200). A failed
        // AI call is surfaced as 502 by the module's exception filter after the run row is stored.
        workspaceGroup.MapPost("/scan", ScanAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        workspaceGroup.MapGet("/runs", ListRunsAsync).RequireAuthorization();

        var runGroup = endpoints.MapGroup("/api/observer/runs/{runId:guid}")
            .WithTags("AiObserver")
            .AddEndpointFilter<DomainExceptionFilter>();

        runGroup.MapGet("/", GetRunAsync).RequireAuthorization();

        return endpoints;
    }

    // ---- handlers ----------------------------------------------------------

    private static async Task<IResult> ScanAsync(
        Guid workspaceId,
        HttpContext http,
        IObserverService observer,
        CancellationToken ct)
    {
        // Manager/Admin is enforced inside the service via the acting user (403 for a member,
        // 404 for an unknown workspace) — same pattern as the Board module.
        var outcome = await observer.ScanAsync(workspaceId, http.RequireUserId(), ct);

        // A Failed run is already persisted (with summary.error); the HTTP contract exposes it as a
        // 502 via the shared domain filter, so the UI can show "AI is unavailable" and reload.
        if (string.Equals(outcome.Status, nameof(ObserverRunStatus.Failed), StringComparison.Ordinal))
        {
            throw new AiProviderException(outcome.Error ?? "Observer scan failed.");
        }

        return Results.Ok(ObserverScanResponse.From(outcome));
    }

    private static async Task<IResult> ListRunsAsync(
        Guid workspaceId,
        int? take,
        HttpContext http,
        IObserverService observer,
        CancellationToken ct)
    {
        // take <= 0 falls back to the service default (20); the service clamps to 1..50.
        var runs = await observer.ListRunsAsync(workspaceId, take ?? 0, http.RequireUserId(), ct);
        return Results.Ok(runs);
    }

    private static async Task<IResult> GetRunAsync(
        Guid runId,
        HttpContext http,
        IObserverService observer,
        CancellationToken ct)
        => Results.Ok(await observer.GetRunAsync(runId, http.RequireUserId(), ct));
}
