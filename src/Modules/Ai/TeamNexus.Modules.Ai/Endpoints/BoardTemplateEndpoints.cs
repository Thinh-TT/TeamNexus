using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Ai.Endpoints;

/// <summary>
/// AI Board Template endpoints (Phase 14 §6, decision D12) under
/// <c>/api/workspaces/{workspaceId}/smart-setup/template</c>.
/// <para>
/// <b>Why workspace-scoped and not board-scoped:</b> the Phase 3 Smart Setup endpoint needs an existing
/// board because it proposes sub-tasks for one. This proposal <i>describes</i> a board, so there is no
/// board id to put in the route yet. Keeping it inside the <c>.../smart-setup/</c> family means the same
/// tags, the same <see cref="DomainExceptionFilter"/> and the same Manager+ rule apply as for the
/// endpoint it extends.
/// </para>
/// </summary>
public static class BoardTemplateEndpoints
{
    public static IEndpointRouteBuilder MapBoardTemplateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/smart-setup/template")
            .WithTags("BoardTemplate")
            .AddEndpointFilter<DomainExceptionFilter>();

        // Generate: never writes. Confirming the proposal is what creates the Pending action.
        group.MapPost("/", GenerateAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        group.MapPost("/confirm", ConfirmAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    // ---- handlers ----------------------------------------------------------

    private static async Task<IResult> GenerateAsync(
        Guid workspaceId,
        BoardTemplateRequest request,
        HttpContext http,
        IBoardTemplateService templates,
        CancellationToken ct)
    {
        // Manager/Admin is enforced inside the service (workspace-scoped role), matching the pattern
        // every other Ai endpoint uses: authentication at the edge, authorization in the service.
        var proposal = await templates.GenerateAsync(workspaceId, request, http.RequireUserId(), ct);
        return Results.Ok(proposal);
    }

    private static async Task<IResult> ConfirmAsync(
        Guid workspaceId,
        ConfirmBoardTemplateRequest request,
        HttpContext http,
        IBoardTemplateService templates,
        CancellationToken ct)
    {
        var log = await templates.ConfirmAsync(workspaceId, request, http.RequireUserId(), ct);

        // 201 Created pointing at the approval route, exactly like the Phase 4 confirm endpoint: the
        // caller now has an id to approve, reject or undo.
        return Results.Created($"/api/ai-actions/{log.Id}", log);
    }
}
