using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>
/// Workspace membership (Phase 3 §3.1 read path; Phase 11 §4.4 management).
/// <para>
/// Reading is Member+ (the data feeds the assignee dropdown of the AI Smart Setup proposal editor
/// and is reused by Phases 4–5). Changing a role or removing a member is <b>Admin only</b>: the
/// roadmap is explicit that a Manager may invite people but not repackage who has which role.
/// </para>
/// <para>
/// "Quick email" (Phase 11 §4.1) lives here too — it is the same Manager-facing workspace surface —
/// under <c>/api/workspaces/{workspaceId}/quick-email</c>, which cannot collide with any
/// <c>/members/{id}</c> route.
/// </para>
/// </summary>
public static class MembersEndpoints
{
    public static IEndpointRouteBuilder MapMembersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/members")
            .WithTags("Members")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapGet("/", GetMembersAsync).RequireAuthorization();

        group.MapPut("/{memberUserId:guid}/role", UpdateMemberRoleAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        group.MapDelete("/{memberUserId:guid}", RemoveMemberAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        // Quick email: one route, own tag, same Manager+ audience as inviting.
        endpoints.MapPost("/api/workspaces/{workspaceId:guid}/quick-email", SendQuickEmailAsync)
            .WithTags("Members")
            .RequireAuthorization()
            .AddEndpointFilter<DomainExceptionFilter>()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    // ---- handlers ----------------------------------------------------------

    private static async Task<IResult> GetMembersAsync(
        Guid workspaceId,
        HttpContext http,
        IWorkspaceMemberService members,
        CancellationToken ct)
    {
        var result = await members.GetMembersAsync(workspaceId, http.RequireUserId(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateMemberRoleAsync(
        Guid workspaceId,
        Guid memberUserId,
        UpdateMemberRoleRequest request,
        HttpContext http,
        IWorkspaceMemberService members,
        CancellationToken ct)
    {
        await members.UpdateMemberRoleAsync(workspaceId, memberUserId, request, http.RequireUserId(), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RemoveMemberAsync(
        Guid workspaceId,
        Guid memberUserId,
        HttpContext http,
        IWorkspaceMemberService members,
        CancellationToken ct)
    {
        await members.RemoveMemberAsync(workspaceId, memberUserId, http.RequireUserId(), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SendQuickEmailAsync(
        Guid workspaceId,
        QuickEmailRequest request,
        HttpContext http,
        IQuickEmailService quickEmail,
        CancellationToken ct)
        => Results.Ok(await quickEmail.SendAsync(workspaceId, request, http.RequireUserId(), ct));
}
