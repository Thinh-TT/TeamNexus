using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Ai.Endpoints;

/// <summary>
/// Accountability Layer endpoints (Phase 4 §3): confirm a Smart Setup proposal into a Pending
/// action, then list / inspect / approve / reject / undo it. Every write path goes through
/// <see cref="IAiActionService"/> — the HTTP layer only authenticates, binds and returns.
/// <para>
/// POST routes require the anti-CSRF header (X-XSRF-TOKEN); all routes sit under
/// <see cref="DomainExceptionFilter"/> so domain errors (400/401/403/404/409) return
/// <c>{ error }</c> with the matching status code.
/// </para>
/// </summary>
public static class AiActionEndpoints
{
    public static IEndpointRouteBuilder MapAiActionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // 1) Confirm a proposal → creates the Pending log (never writes tasks/labels).
        //    Same prefix as the Phase 3 generate endpoint; the routes differ so both coexist.
        var confirmGroup = endpoints.MapGroup("/api/boards/{boardId:guid}/smart-setup")
            .WithTags("AiActions")
            .AddEndpointFilter<DomainExceptionFilter>();

        confirmGroup.MapPost("/confirm", ConfirmAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        // 2) Board-scoped action history.
        var historyGroup = endpoints.MapGroup("/api/boards/{boardId:guid}/ai-actions")
            .WithTags("AiActions")
            .AddEndpointFilter<DomainExceptionFilter>();

        historyGroup.MapGet("/", ListAsync).RequireAuthorization();

        // 3) One action + its decisions.
        var actionGroup = endpoints.MapGroup("/api/ai-actions/{logId:guid}")
            .WithTags("AiActions")
            .AddEndpointFilter<DomainExceptionFilter>();

        actionGroup.MapGet("/", GetAsync).RequireAuthorization();

        actionGroup.MapPost("/approve", ApproveAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        actionGroup.MapPost("/reject", RejectAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        actionGroup.MapPost("/undo", UndoAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    // ---- handlers ----------------------------------------------------------

    private static async Task<IResult> ConfirmAsync(
        Guid boardId,
        ConfirmSmartSetupRequest request,
        HttpContext http,
        IAiActionService actions,
        CancellationToken ct)
    {
        var log = await actions.RequestCreateSubtasksAsync(boardId, request, http.RequireUserId(), ct);
        return Results.Created($"/api/ai-actions/{log.Id}", log);
    }

    private static async Task<IResult> ListAsync(
        Guid boardId,
        string? status,
        int? take,
        HttpContext http,
        IAiActionService actions,
        CancellationToken ct)
    {
        // take <= 0 falls back to the service default (20); the service clamps to 1..100.
        var logs = await actions.ListAsync(boardId, ParseStatus(status), take ?? 0, http.RequireUserId(), ct);
        return Results.Ok(logs);
    }

    private static async Task<IResult> GetAsync(
        Guid logId,
        HttpContext http,
        IAiActionService actions,
        CancellationToken ct)
        => Results.Ok(await actions.GetAsync(logId, http.RequireUserId(), ct));

    private static async Task<IResult> ApproveAsync(
        Guid logId,
        HttpContext http,
        IAiActionService actions,
        CancellationToken ct)
        => Results.Ok(await actions.ApproveAsync(logId, http.RequireUserId(), ct));

    // Body is nullable on purpose: rejecting without a reason is valid, so an empty body must
    // not fail binding with a framework 400.
    private static async Task<IResult> RejectAsync(
        Guid logId,
        RejectAiActionRequest? request,
        HttpContext http,
        IAiActionService actions,
        CancellationToken ct)
        => Results.Ok(await actions.RejectAsync(logId, request?.Note, http.RequireUserId(), ct));

    private static async Task<IResult> UndoAsync(
        Guid logId,
        HttpContext http,
        IAiActionService actions,
        CancellationToken ct)
        => Results.Ok(await actions.UndoAsync(logId, http.RequireUserId(), ct));

    // ---- helpers -----------------------------------------------------------

    /// <summary>
    /// Strict, name-only status filter parse: any casing is accepted, numeric enum values
    /// (e.g. "2") are rejected so a typo cannot silently select a status. Invalid input is a
    /// domain 400 → <c>{ error }</c> (binding the enum directly would produce a framework 400).
    /// </summary>
    private static AiActionStatus? ParseStatus(string? status)
    {
        var value = status?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        foreach (var name in Enum.GetNames<AiActionStatus>())
        {
            if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase))
            {
                return Enum.Parse<AiActionStatus>(name);
            }
        }

        throw new BadRequestException(
            $"Unknown status '{value}'. Expected one of: {string.Join(", ", Enum.GetNames<AiActionStatus>())}.");
    }
}
