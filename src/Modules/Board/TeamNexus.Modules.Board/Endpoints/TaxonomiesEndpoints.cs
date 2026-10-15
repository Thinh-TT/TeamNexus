using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>
/// The fixed workspace vocabulary Jev answers <c>Choice</c> questions against (Giai đoạn 14.7 §A2).
/// <para>
/// Reading is Member+ — every member sees the domain and work-type pills on a card, and the AI dispatching
/// context is built from the same list. Replacing it is Manager+, the same gate as every other
/// workspace-configuration write.
/// </para>
/// <para>
/// The <c>PUT</c> is a <b>replace</b>, not a patch: the client sends the vocabulary it wants to exist after
/// the call. That keeps the operation idempotent and removes any need for a diff protocol.
/// </para>
/// </summary>
public static class TaxonomiesEndpoints
{
    public static IEndpointRouteBuilder MapTaxonomiesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/taxonomies")
            .WithTags("Taxonomies")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapGet("/", GetAsync).RequireAuthorization();

        group.MapPut("/", ReplaceAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        Guid workspaceId,
        HttpContext http,
        ITaxonomyService taxonomies,
        CancellationToken ct)
        => Results.Ok(await taxonomies.GetAsync(workspaceId, http.RequireUserId(), ct));

    private static async Task<IResult> ReplaceAsync(
        Guid workspaceId,
        UpdateTaxonomyRequest request,
        HttpContext http,
        ITaxonomyService taxonomies,
        CancellationToken ct)
        => Results.Ok(await taxonomies.ReplaceAsync(workspaceId, request, http.RequireUserId(), ct));
}
