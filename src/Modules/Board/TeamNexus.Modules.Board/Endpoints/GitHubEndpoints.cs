using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services.Integrations;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>
/// Workspace GitHub integration endpoints (Phase 15 §1.1) under
/// <c>/api/workspaces/{workspaceId}/integrations/github</c>.
/// Restricts access to workspace Managers and Admins.
/// </summary>
public static class GitHubEndpoints
{
    public static IEndpointRouteBuilder MapGitHubEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/integrations/github")
            .WithTags("Workspace Integrations")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapGet("/", GetIntegrationAsync)
            .RequireAuthorization();

        group.MapPut("/", UpdateIntegrationAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        group.MapDelete("/", DeleteIntegrationAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        group.MapPost("/test", TestConnectionAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    // ---- handlers ----------------------------------------------------------

    private static async Task<IResult> GetIntegrationAsync(
        Guid workspaceId,
        HttpContext http,
        IGitHubIntegrationService service,
        CancellationToken ct)
    {
        var result = await service.GetAsync(workspaceId, http.RequireUserId(), ct);
        return result is null
            ? Results.NotFound(new { error = "GitHub integration is not configured for this workspace." })
            : Results.Ok(result);
    }

    private static async Task<IResult> UpdateIntegrationAsync(
        Guid workspaceId,
        UpdateGitHubIntegrationRequest request,
        HttpContext http,
        IGitHubIntegrationService service,
        CancellationToken ct)
    {
        var result = await service.UpsertAsync(workspaceId, http.RequireUserId(), request, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> DeleteIntegrationAsync(
        Guid workspaceId,
        HttpContext http,
        IGitHubIntegrationService service,
        CancellationToken ct)
    {
        await service.DeleteAsync(workspaceId, http.RequireUserId(), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> TestConnectionAsync(
        Guid workspaceId,
        HttpContext http,
        IGitHubIntegrationService service,
        CancellationToken ct)
    {
        var result = await service.TestConnectionAsync(workspaceId, http.RequireUserId(), ct);
        return Results.Ok(result);
    }
}
