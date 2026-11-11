using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Ai.DTOs.Integrations;
using TeamNexus.Modules.Ai.Services.Integrations;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Ai.Endpoints;

/// <summary>
/// Minimal APIs for managing workspace outbound Slack/Discord webhooks, sending test pings,
/// and inspecting the delivery outbox history (Phase 15.2 §T15.2.4).
/// </summary>
public static class WorkspaceWebhooksEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceWebhooksEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/integrations/webhooks")
            .WithTags("Integrations")
            .AddEndpointFilter<DomainExceptionFilter>()
            .RequireAuthorization();

        group.MapGet("/", ListAsync);

        group.MapPost("/", CreateAsync)
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        group.MapPatch("/{endpointId:guid}", UpdateAsync)
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        group.MapDelete("/{endpointId:guid}", DeleteAsync)
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        group.MapPost("/{endpointId:guid}/test", SendTestAsync)
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        group.MapGet("/{endpointId:guid}/deliveries", GetDeliveriesAsync);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid workspaceId,
        HttpContext http,
        IWorkspaceWebhookService webhooks,
        CancellationToken ct)
        => Results.Ok(await webhooks.ListAsync(workspaceId, http.RequireUserId(), ct));

    private static async Task<IResult> CreateAsync(
        Guid workspaceId,
        CreateWorkspaceWebhookRequest request,
        HttpContext http,
        IWorkspaceWebhookService webhooks,
        CancellationToken ct)
    {
        var created = await webhooks.CreateAsync(workspaceId, request, http.RequireUserId(), ct);
        return Results.Created(
            $"/api/workspaces/{workspaceId}/integrations/webhooks/{created.Id}",
            created);
    }

    private static async Task<IResult> UpdateAsync(
        Guid workspaceId,
        Guid endpointId,
        UpdateWorkspaceWebhookRequest request,
        HttpContext http,
        IWorkspaceWebhookService webhooks,
        CancellationToken ct)
        => Results.Ok(await webhooks.UpdateAsync(workspaceId, endpointId, request, http.RequireUserId(), ct));

    private static async Task<IResult> DeleteAsync(
        Guid workspaceId,
        Guid endpointId,
        HttpContext http,
        IWorkspaceWebhookService webhooks,
        CancellationToken ct)
    {
        await webhooks.DeleteAsync(workspaceId, endpointId, http.RequireUserId(), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SendTestAsync(
        Guid workspaceId,
        Guid endpointId,
        HttpContext http,
        IWorkspaceWebhookService webhooks,
        CancellationToken ct)
        => Results.Ok(await webhooks.SendTestAsync(workspaceId, endpointId, http.RequireUserId(), ct));

    private static async Task<IResult> GetDeliveriesAsync(
        Guid workspaceId,
        Guid endpointId,
        HttpContext http,
        IWorkspaceWebhookService webhooks,
        CancellationToken ct)
        => Results.Ok(await webhooks.GetDeliveriesAsync(workspaceId, endpointId, http.RequireUserId(), ct));
}

