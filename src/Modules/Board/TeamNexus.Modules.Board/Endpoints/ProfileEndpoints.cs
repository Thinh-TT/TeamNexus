using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>
/// The signed-in user's own profile (Phase 11 §5.4) under <c>/api/users/me</c>.
/// <para>
/// Every route is <b>caller-scoped</b>: the path never contains a user id, so there is no way to read
/// or modify somebody else's profile here. <c>GET /api/auth/me</c> is left untouched — it is the
/// session bootstrap the frontend store calls on every load.
/// </para>
/// </summary>
public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/users/me")
            .WithTags("Profile")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapGet("/", GetAsync).RequireAuthorization();

        group.MapPut("/", UpdateAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        group.MapGet("/workspaces", ListWorkspacesAsync).RequireAuthorization();

        group.MapDelete("/workspaces/{workspaceId:guid}", LeaveAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    // ---- handlers ----------------------------------------------------------

    private static async Task<IResult> GetAsync(
        HttpContext http,
        IUserProfileService profiles,
        CancellationToken ct)
        => Results.Ok(await profiles.GetAsync(http.RequireUserId(), ct));

    private static async Task<IResult> UpdateAsync(
        UpdateProfileRequest request,
        HttpContext http,
        IUserProfileService profiles,
        CancellationToken ct)
        => Results.Ok(await profiles.UpdateAsync(request, http.RequireUserId(), ct));

    private static async Task<IResult> ListWorkspacesAsync(
        HttpContext http,
        IUserProfileService profiles,
        CancellationToken ct)
        => Results.Ok(await profiles.ListMyWorkspacesAsync(http.RequireUserId(), ct));

    private static async Task<IResult> LeaveAsync(
        Guid workspaceId,
        HttpContext http,
        IUserProfileService profiles,
        CancellationToken ct)
    {
        await profiles.LeaveWorkspaceAsync(workspaceId, http.RequireUserId(), ct);
        return Results.NoContent();
    }
}
