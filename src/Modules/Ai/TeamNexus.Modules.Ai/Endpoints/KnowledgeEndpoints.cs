using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Services.Knowledge;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Ai.Endpoints;

/// <summary>
/// The workspace knowledge base (Giai đoạn 14.7 Track B §B3) under
/// <c>/api/workspaces/{workspaceId}/knowledge/documents</c>.
/// <para>
/// <b>Reading is Member+, writing is Manager+.</b> Every member benefits from the AI knowing the project's
/// documents, but changing what the AI can see is workspace configuration — the same gate as taxonomies and the
/// board template.
/// </para>
/// <para>
/// <b>Both writes return 201 with a <c>Pending</c> action, never the applied result.</b> Adding a document means
/// embedding it (money) and changes what the AI can read, so it goes through the Accountability Layer like every
/// other data-writing AI feature: the caller receives a proposal, and approving it is what writes.
/// </para>
/// </summary>
public static class KnowledgeEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/knowledge/documents")
            .WithTags("Knowledge")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapGet("/", ListAsync).RequireAuthorization();

        group.MapPost("/", CreateAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        group.MapDelete("/{documentId:guid}", DeleteAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid workspaceId,
        HttpContext http,
        IKnowledgeBaseService knowledge,
        CancellationToken ct)
        => Results.Ok(await knowledge.ListAsync(workspaceId, http.RequireUserId(), ct));

    private static async Task<IResult> CreateAsync(
        Guid workspaceId,
        CreateKnowledgeDocumentRequest request,
        HttpContext http,
        IKnowledgeBaseService knowledge,
        CancellationToken ct)
    {
        var log = await knowledge.CreateAsync(workspaceId, request, http.RequireUserId(), ct);

        // 201 with the ACTION, not the document: nothing has been indexed yet, and answering with a document
        // would tell the client the upload succeeded when a Manager has still to approve it.
        return Results.Created($"/api/ai-actions/{log.Id}", log);
    }

    private static async Task<IResult> DeleteAsync(
        Guid workspaceId,
        Guid documentId,
        HttpContext http,
        IKnowledgeBaseService knowledge,
        CancellationToken ct)
    {
        var log = await knowledge.DeleteAsync(workspaceId, documentId, http.RequireUserId(), ct);
        return Results.Created($"/api/ai-actions/{log.Id}", log);
    }
}
