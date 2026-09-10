using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Ai.Endpoints;

/// <summary>
/// Smart Setup endpoint (Phase 3 §3.2) under /api/boards/{boardId}/smart-setup.
/// <para>
/// POST only, and deliberately the ONLY Ai endpoint in Phase 3: it proposes sub-tasks and
/// never writes to the database. Applying a confirmed proposal is Phase 4
/// (<c>POST .../smart-setup/confirm</c> → <c>AiActionService</c>, Pending → Approve).
/// </para>
/// </summary>
public static class SmartSetupEndpoints
{
    public static IEndpointRouteBuilder MapSmartSetupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/boards/{boardId:guid}/smart-setup")
            .WithTags("SmartSetup")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapPost("/", GenerateAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    private static async Task<IResult> GenerateAsync(
        Guid boardId,
        SmartSetupRequest request,
        HttpContext http,
        ISmartSetupService smartSetup,
        CancellationToken ct)
    {
        // Board's RequireUserId helper is internal to that module, so read the claim here —
        // same approach as Program.cs. A missing/invalid claim is 401 via UnauthorizedException.
        var userIdClaim = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedException();
        }

        // Manager/Admin is enforced inside the service (workspace-scoped role), matching the
        // Board module: authentication here, authorization there.
        var proposal = await smartSetup.GenerateAsync(boardId, request, userId, ct);
        return Results.Ok(proposal);
    }
}
