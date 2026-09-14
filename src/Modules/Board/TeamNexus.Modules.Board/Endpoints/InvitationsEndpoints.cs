using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>
/// Workspace invitations (Phase 11 §3.3): the anonymous accept surface under <c>/api/invitations</c>
/// plus the Manager-facing management surface under <c>/api/workspaces/{workspaceId}/invitations</c>.
/// <para>
/// <b>Preview is anonymous, accept is not.</b> The preview only tells a visitor which workspace they
/// were invited to, so the page can render before sign-in; accepting requires an authenticated
/// caller whose email matches the invitation, which is what makes the link non-transferable.
/// </para>
/// <para>
/// Accept is a <c>POST</c> even though the token arrives from a URL: it writes a membership, and a
/// GET would let a mail client's link preview (or any crawler) join a workspace without a human
/// clicking anything.
/// </para>
/// </summary>
public static class InvitationsEndpoints
{
    public static IEndpointRouteBuilder MapInvitationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // ---- anonymous accept surface ------------------------------------------
        var publicGroup = endpoints.MapGroup("/api/invitations")
            .WithTags("Invitations")
            .AddEndpointFilter<DomainExceptionFilter>();

        publicGroup.MapGet("/preview", PreviewAsync).AllowAnonymous();

        publicGroup.MapPost("/accept", AcceptAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        // ---- Manager/Admin management surface ----------------------------------
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/invitations")
            .WithTags("Invitations")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapGet("/", ListAsync).RequireAuthorization();

        group.MapPost("/", CreateAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        group.MapDelete("/{invitationId:guid}", CancelAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    // ---- handlers ----------------------------------------------------------

    private static async Task<IResult> PreviewAsync(
        string? token,
        IInvitationService invitations,
        CancellationToken ct)
        => Results.Ok(await invitations.PreviewAsync(token ?? string.Empty, ct));

    private static async Task<IResult> AcceptAsync(
        AcceptTokenRequest request,
        HttpContext http,
        IInvitationService invitations,
        CancellationToken ct)
    {
        var result = await invitations.AcceptAsync(
            request.Token,
            http.RequireUserId(),
            http.GetUserEmail(),
            ct);

        return Results.Ok(result);
    }

    private static async Task<IResult> ListAsync(
        Guid workspaceId,
        HttpContext http,
        IInvitationService invitations,
        CancellationToken ct)
        => Results.Ok(await invitations.ListAsync(workspaceId, http.RequireUserId(), ct));

    private static async Task<IResult> CreateAsync(
        Guid workspaceId,
        CreateInvitationRequest request,
        HttpContext http,
        IInvitationService invitations,
        CancellationToken ct)
    {
        var created = await invitations.CreateAsync(workspaceId, request, http.RequireUserId(), ct);

        return Results.Created($"/api/workspaces/{workspaceId}/invitations/{created.Id}", created);
    }

    private static async Task<IResult> CancelAsync(
        Guid workspaceId,
        Guid invitationId,
        HttpContext http,
        IInvitationService invitations,
        CancellationToken ct)
    {
        await invitations.CancelAsync(workspaceId, invitationId, http.RequireUserId(), ct);
        return Results.NoContent();
    }
}

/// <summary>
/// Accept payload. The token travels in the body rather than the URL so it never lands in a proxy
/// or web-server access log, and so the POST is antiforgery-protected like every other write.
/// </summary>
public sealed record AcceptTokenRequest(string Token);
