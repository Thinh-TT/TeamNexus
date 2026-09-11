using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Ai.Services.Agent;
using TeamNexus.Modules.Board.Endpoints;

namespace TeamNexus.Modules.Ai.Endpoints;

/// <summary>
/// Agent output file endpoints (Phase 7 §4.9), routes 6–7.
/// <para>
/// Both are <b>GET</b> — read-only and idempotent — so neither carries the anti-CSRF filter, and using
/// GET also lets the frontend's <c>httpClient</c> refresh a 401 and retry transparently while it
/// downloads with <c>responseType: 'blob'</c> (the Phase 6 lesson: <c>window.open</c> cannot do that).
/// </para>
/// </summary>
public static class AttachmentEndpoints
{
    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/tasks/{taskId:guid}/attachments")
            .WithTags("Attachments")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapGet("/", ListAsync).RequireAuthorization();
        group.MapGet("/{attachmentId:guid}/download", DownloadAsync).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid taskId,
        HttpContext http,
        IAgentAttachmentService attachments,
        CancellationToken ct)
        => Results.Ok(await attachments.ListAsync(taskId, http.RequireUserId(), ct));

    /// <summary>
    /// Returns the bytes held in RAM. <c>Results.File</c> sets <c>Content-Length</c> and an ASCII
    /// <c>Content-Disposition</c> (the stored name is already a sanitized slug), exactly like the
    /// Phase 6 report export.
    /// </summary>
    private static async Task<IResult> DownloadAsync(
        Guid taskId,
        Guid attachmentId,
        HttpContext http,
        IAgentAttachmentService attachments,
        CancellationToken ct)
    {
        var (content, contentType, fileName) =
            await attachments.DownloadAsync(taskId, attachmentId, http.RequireUserId(), ct);

        return Results.File(content, contentType, fileDownloadName: fileName);
    }
}
