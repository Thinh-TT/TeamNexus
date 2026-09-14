using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>
/// Workspace metadata, ownership and activity feed (Phase 10 §2.3) under <c>/api/workspaces</c>.
/// <para>
/// <b>History:</b> <c>GET /api/workspaces</c> used to be an inline minimal-API handler inside
/// <c>Program.cs</c>. Phase 10 moved it into <see cref="IWorkspaceService"/> (decision D1) and added
/// the mutations the roadmap's "Workspace Settings" box needs.
/// </para>
/// <para>
/// Thin by design: parse the route/body → call the service → map the result. Validation, the
/// 403/404 role gates and the 400 rules all live in the services and surface as
/// <c>{ "error": "…" }</c> through <see cref="DomainExceptionFilter"/>. Non-GET routes also carry
/// <c>AntiforgeryValidationEndpointFilter</c>, exactly like every other mutating Board endpoint;
/// the activity read is a GET and is therefore not CSRF-protected (Phase 6 §0 D12).
/// </para>
/// </summary>
public static class WorkspacesEndpoints
{
    public static IEndpointRouteBuilder MapWorkspacesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces")
            .WithTags("Workspaces")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapGet("/", GetWorkspacesAsync).RequireAuthorization();
        group.MapGet("/{workspaceId:guid}", GetWorkspaceAsync).RequireAuthorization();
        group.MapPut("/{workspaceId:guid}", UpdateWorkspaceAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();
        group.MapPut("/{workspaceId:guid}/owner", TransferOwnershipAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();
        group.MapDelete("/{workspaceId:guid}", DeleteWorkspaceAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();
        group.MapGet("/{workspaceId:guid}/activity", GetActivityAsync).RequireAuthorization();

        return endpoints;
    }

    // ---- handlers ----------------------------------------------------------

    private static async Task<IResult> GetWorkspacesAsync(
        HttpContext http,
        IWorkspaceService workspaces,
        CancellationToken ct)
    {
        var result = await workspaces.ListForUserAsync(http.RequireUserId(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetWorkspaceAsync(
        Guid workspaceId,
        HttpContext http,
        IWorkspaceService workspaces,
        CancellationToken ct)
    {
        var workspace = await workspaces.GetAsync(workspaceId, http.RequireUserId(), ct);
        return Results.Ok(workspace);
    }

    private static async Task<IResult> UpdateWorkspaceAsync(
        Guid workspaceId,
        UpdateWorkspaceRequest request,
        HttpContext http,
        IWorkspaceService workspaces,
        CancellationToken ct)
    {
        await workspaces.UpdateAsync(workspaceId, request, http.RequireUserId(), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> TransferOwnershipAsync(
        Guid workspaceId,
        TransferOwnershipRequest request,
        HttpContext http,
        IWorkspaceService workspaces,
        CancellationToken ct)
    {
        await workspaces.TransferOwnershipAsync(workspaceId, request, http.RequireUserId(), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteWorkspaceAsync(
        Guid workspaceId,
        HttpContext http,
        IWorkspaceService workspaces,
        CancellationToken ct)
    {
        await workspaces.DeleteAsync(workspaceId, http.RequireUserId(), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetActivityAsync(
        Guid workspaceId,
        Guid? boardId,
        string? entityType,
        string? action,
        int? take,
        string? before,
        HttpContext http,
        IWorkspaceActivityService activity,
        CancellationToken ct)
    {
        var page = await activity.GetAsync(
            workspaceId,
            http.RequireUserId(),
            boardId,
            entityType,
            action,
            take,
            before,
            ct);

        return Results.Ok(page);
    }
}
