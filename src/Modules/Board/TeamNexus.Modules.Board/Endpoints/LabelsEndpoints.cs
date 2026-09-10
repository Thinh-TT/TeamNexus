using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>
/// Labels (Phase 2 §2.5): workspace-scoped CRUD under /api/workspaces/{workspaceId}/labels
/// and attach/detach under /api/tasks/{taskId}/labels.
/// </summary>
public static class LabelsEndpoints
{
    public static IEndpointRouteBuilder MapLabelsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var workspaceGroup = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/labels")
            .WithTags("Labels")
            .AddEndpointFilter<DomainExceptionFilter>();

        workspaceGroup.MapGet("/", GetLabelsAsync).RequireAuthorization();
        workspaceGroup.MapPost("/", CreateLabelAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();
        workspaceGroup.MapDelete("/{labelId:guid}", DeleteLabelAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        var taskGroup = endpoints.MapGroup("/api/tasks/{taskId:guid}/labels")
            .WithTags("Labels")
            .AddEndpointFilter<DomainExceptionFilter>();

        taskGroup.MapPost("/", AttachLabelAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();
        taskGroup.MapDelete("/{labelId:guid}", DetachLabelAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    private static async Task<IResult> GetLabelsAsync(
        Guid workspaceId,
        HttpContext http,
        ILabelService labels,
        CancellationToken ct)
    {
        var result = await labels.GetLabelsAsync(workspaceId, http.RequireUserId(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateLabelAsync(
        Guid workspaceId,
        CreateLabelRequest request,
        HttpContext http,
        ILabelService labels,
        CancellationToken ct)
    {
        var label = await labels.CreateLabelAsync(workspaceId, request, http.RequireUserId(), ct);
        return Results.Created($"/api/workspaces/{workspaceId}/labels/{label.Id}", label);
    }

    private static async Task<IResult> DeleteLabelAsync(
        Guid workspaceId,
        Guid labelId,
        HttpContext http,
        ILabelService labels,
        CancellationToken ct)
    {
        await labels.DeleteLabelAsync(workspaceId, labelId, http.RequireUserId(), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> AttachLabelAsync(
        Guid taskId,
        AttachLabelRequest request,
        HttpContext http,
        ILabelService labels,
        CancellationToken ct)
    {
        await labels.AttachToTaskAsync(taskId, request, http.RequireUserId(), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DetachLabelAsync(
        Guid taskId,
        Guid labelId,
        HttpContext http,
        ILabelService labels,
        CancellationToken ct)
    {
        await labels.DetachFromTaskAsync(taskId, labelId, http.RequireUserId(), ct);
        return Results.NoContent();
    }
}
