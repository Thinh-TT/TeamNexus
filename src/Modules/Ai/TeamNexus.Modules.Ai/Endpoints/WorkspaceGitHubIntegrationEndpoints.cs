using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Ai.DTOs.Integrations;
using TeamNexus.Modules.Ai.Services.Integrations;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Ai.Endpoints;

/// <summary>
/// Minimal APIs for managing workspace GitHub integration settings, rotating secrets,
/// and inspecting delivery logs (Phase 15.3 §T15.3.2).
/// </summary>
public static class WorkspaceGitHubIntegrationEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceGitHubIntegrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/integrations/github")
            .WithTags("Integrations")
            .AddEndpointFilter<DomainExceptionFilter>()
            .RequireAuthorization();

        group.MapGet("/", GetAsync);

        group.MapPut("/", UpsertAsync)
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        group.MapPost("/rotate-secret", RotateSecretAsync)
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        group.MapDelete("/", DeleteAsync)
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        group.MapGet("/deliveries", GetDeliveriesAsync);

        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        Guid workspaceId,
        HttpContext http,
        IWorkspaceGitHubIntegrationService service,
        CancellationToken ct)
    {
        var config = await service.GetAsync(workspaceId, http.RequireUserId(), ct);
        return config is null
            ? Results.NotFound(new { error = "Tích hợp GitHub chưa được cấu hình cho Không gian làm việc này." })
            : Results.Ok(config);
    }

    private static async Task<IResult> UpsertAsync(
        Guid workspaceId,
        UpsertWorkspaceGitHubIntegrationRequest request,
        HttpContext http,
        IWorkspaceGitHubIntegrationService service,
        CancellationToken ct)
    {
        var config = await service.UpsertAsync(workspaceId, request, http.RequireUserId(), ct);
        return Results.Ok(config);
    }

    private static async Task<IResult> RotateSecretAsync(
        Guid workspaceId,
        HttpContext http,
        IWorkspaceGitHubIntegrationService service,
        CancellationToken ct)
    {
        var rotated = await service.RotateSecretAsync(workspaceId, http.RequireUserId(), ct);
        return Results.Ok(rotated);
    }

    private static async Task<IResult> DeleteAsync(
        Guid workspaceId,
        HttpContext http,
        IWorkspaceGitHubIntegrationService service,
        CancellationToken ct)
    {
        await service.DeleteAsync(workspaceId, http.RequireUserId(), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetDeliveriesAsync(
        Guid workspaceId,
        [FromQuery] int? take,
        HttpContext http,
        IWorkspaceGitHubIntegrationService service,
        CancellationToken ct)
    {
        var deliveries = await service.GetDeliveriesAsync(workspaceId, http.RequireUserId(), take ?? 30, ct);
        return Results.Ok(deliveries);
    }
}
