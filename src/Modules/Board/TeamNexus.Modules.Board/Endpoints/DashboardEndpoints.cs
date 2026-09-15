using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>
/// Workspace dashboard read model (Phase 12 §1) under
/// <c>/api/workspaces/{workspaceId}/dashboard</c>.
/// <para>
/// A <b>GET</b>, so no CSRF filter — the same rule as every other read in the Board module. The
/// dashboard is visible to <b>any member</b> (it summarises work the member already sees on the
/// boards); the Manager-only gate belongs to the full activity log, not to this overview.
/// </para>
/// </summary>
public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces")
            .WithTags("Dashboard")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapGet("/{workspaceId:guid}/dashboard", GetDashboardAsync).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> GetDashboardAsync(
        Guid workspaceId,
        int? days,
        int? take,
        HttpContext http,
        IDashboardService dashboard,
        CancellationToken ct)
    {
        var result = await dashboard.GetAsync(workspaceId, http.RequireUserId(), days, take, ct);
        return Results.Ok(result);
    }
}
