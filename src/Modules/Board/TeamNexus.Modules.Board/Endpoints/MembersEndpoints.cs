using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Board.Services;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>
/// Workspace membership lookup (Phase 3 §3.1) under /api/workspaces/{workspaceId}/members.
/// Member+ can read it; the data feeds the assignee dropdown of the AI Smart Setup proposal
/// editor (§5) and is reused by Phases 4–5.
/// </summary>
public static class MembersEndpoints
{
    public static IEndpointRouteBuilder MapMembersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/members")
            .WithTags("Members")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapGet("/", GetMembersAsync).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> GetMembersAsync(
        Guid workspaceId,
        HttpContext http,
        IWorkspaceMemberService members,
        CancellationToken ct)
    {
        var result = await members.GetMembersAsync(workspaceId, http.RequireUserId(), ct);
        return Results.Ok(result);
    }
}
