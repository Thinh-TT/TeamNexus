using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Ai.Endpoints;

/// <summary>
/// Board-scoped AI Agent Chat endpoints (Phase 14.5 §B) under <c>/api/boards/{boardId}/agent-chat</c>.
/// <para>
/// <b>Why a POST stream.</b> The client owns the turn it is asking and replays the transcript, and every
/// mutating verb in this codebase carries <c>AntiforgeryValidationEndpointFilter</c> — a POST keeps the
/// existing CSRF contract instead of inventing a query-string token. The response is read with
/// <c>fetch</c>, never <c>EventSource</c> (which can neither POST nor set a header).
/// </para>
/// <para>
/// <b>The frame contract is shared with the task chat</b> (<c>meta</c> → <c>delta</c>* → <c>done</c> |
/// <c>error</c>, written by <c>AiChatSseWriter</c>). One transport shape for both surfaces means the
/// frontend reuses its parser and a third chat could be added without a second protocol.
/// </para>
/// <para>
/// Failures <b>before</b> the response starts (503/404/400) are ordinary JSON errors from
/// <see cref="DomainExceptionFilter"/>; failures after that leave as an SSE <c>error</c> frame — see
/// <see cref="SseStreamResult"/>.
/// </para>
/// </summary>
public static class AgentChatEndpoints
{
    public static IEndpointRouteBuilder MapAgentChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/boards/{boardId:guid}/agent-chat")
            .WithTags("AgentChat")
            .AddEndpointFilter<DomainExceptionFilter>();

        // Idempotent: "the session" is the stored turns of this board, so opening the panel twice must not
        // create a second conversation. It exists so the client has one call that proves access and returns
        // the workspace context before it renders anything.
        group.MapGet("/session", GetSessionAsync).RequireAuthorization();

        group.MapGet("/messages", ListAsync).RequireAuthorization();

        group.MapPost("/stream", StreamAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        group.MapPost("/message", SaveAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        group.MapPost("/proposal/confirm", ConfirmProposalAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    // ---- handlers ----------------------------------------------------------

    private static async Task<IResult> GetSessionAsync(
        Guid boardId,
        HttpContext http,
        IAgentChatService chat,
        CancellationToken ct)
    {
        // Reading the (empty or populated) transcript is the cheapest membership probe, and it is the exact
        // call the UI needs anyway — so no dedicated "does this board exist" endpoint is required.
        var messages = await chat.ListMessagesAsync(boardId, null, 1, http.RequireUserId(), ct);

        return Results.Ok(new
        {
            boardId,
            hasMessages = messages.Count > 0,
            model = chat.ModelName,
        });
    }

    private static async Task<IResult> ListAsync(
        Guid boardId,
        DateTimeOffset? before,
        int? take,
        HttpContext http,
        IAgentChatService chat,
        CancellationToken ct)
    {
        var messages = await chat.ListMessagesAsync(
            boardId, before, take ?? 0, http.RequireUserId(), ct);

        return Results.Ok(messages);
    }

    private static async Task<IResult> StreamAsync(
        Guid boardId,
        AgentChatSendRequest request,
        HttpContext http,
        IAgentChatService chat,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var userId = http.RequireUserId();

        // AWAITED on purpose: every guard lives here, so 503/404/403/400 still become real status codes
        // through DomainExceptionFilter instead of a stream that has already claimed 200.
        var prepared = await chat.PrepareAsync(boardId, request, userId, ct);

        return new SseStreamResult(
            chat.StreamPreparedAsync(prepared, http.RequestAborted),
            loggerFactory.CreateLogger(typeof(AgentChatEndpoints)));
    }

    private static async Task<IResult> SaveAsync(
        Guid boardId,
        SaveAgentChatAnswerRequest request,
        HttpContext http,
        IAgentChatService chat,
        CancellationToken ct)
    {
        var log = await chat.SaveAnswerAsync(boardId, request, http.RequireUserId(), ct);
        return Results.Created($"/api/ai-actions/{log.Id}", log);
    }

    private static async Task<IResult> ConfirmProposalAsync(
        Guid boardId,
        SubmitAgentChatProposalRequest request,
        HttpContext http,
        IAgentChatService chat,
        CancellationToken ct)
    {
        var log = await chat.SubmitProposalAsync(boardId, request, http.RequireUserId(), ct);
        return Results.Created($"/api/ai-actions/{log.Id}", log);
    }
}
