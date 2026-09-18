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
/// AI Task Chat endpoints (Phase 14 §2.2/§2.3) under <c>/api/tasks/{taskId}/ai-chat</c>.
/// <para>
/// <b>Why the stream is a POST:</b> the client owns the transcript and sends it on every turn, and every
/// mutating verb in this codebase carries <c>AntiforgeryValidationEndpointFilter</c>. A POST keeps the
/// existing CSRF contract instead of inventing a query-string token, and the client reads the response
/// with <c>fetch</c> (not <c>EventSource</c>, which can neither POST nor set a header).
/// </para>
/// <para>
/// Failures that happen <b>before</b> the response starts (503/404/400) are ordinary JSON error
/// responses from <see cref="DomainExceptionFilter"/>. Failures after that point leave as an SSE
/// <c>error</c> frame — see <see cref="SseStreamResult"/>.
/// </para>
/// </summary>
public static class AiChatEndpoints
{
    public static IEndpointRouteBuilder MapAiChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/tasks/{taskId:guid}/ai-chat")
            .WithTags("AiChat")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapPost("/stream", StreamAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        group.MapPost("/message", SaveAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    // ---- handlers ----------------------------------------------------------

    private static async Task<IResult> StreamAsync(
        Guid taskId,
        AiChatSendRequest request,
        HttpContext http,
        IAiChatService chat,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var userId = http.RequireUserId();

        // AWAITED on purpose: every guard lives here, so 503/404/403/400 still become real status codes
        // through DomainExceptionFilter instead of a stream that has already claimed 200.
        var prepared = await chat.PrepareAsync(taskId, request, userId, ct);

        return new SseStreamResult(
            chat.StreamPreparedAsync(prepared, http.RequestAborted),
            loggerFactory.CreateLogger(typeof(AiChatEndpoints)));
    }

    private static async Task<IResult> SaveAsync(
        Guid taskId,
        SaveAiChatMessageRequest request,
        HttpContext http,
        IAiChatService chat,
        CancellationToken ct)
    {
        var log = await chat.SaveAnswerAsync(taskId, request, http.RequireUserId(), ct);
        return Results.Created($"/api/ai-actions/{log.Id}", log);
    }
}
