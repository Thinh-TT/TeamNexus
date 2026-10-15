using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Ai.Endpoints;

/// <summary>
/// Jev-backed triage and assignee dispatching (Giai đoạn 14.7 §A3/§A4).
/// <para>
/// Both are <b>read-only</b>: they return suggestions and write nothing, so neither carries an antiforgery
/// filter beyond the usual POST rule and neither enters the Accountability Layer. Applying a suggestion is
/// an ordinary task edit by the user.
/// </para>
/// <para>
/// POST rather than GET for triage because the input is the draft the user is typing (title + description),
/// which does not belong in a URL — it would end up in access logs and browser history.
/// </para>
/// </summary>
public static class TaskTriageEndpoints
{
    public static IEndpointRouteBuilder MapTaskTriageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/boards/{boardId:guid}/tasks")
            .WithTags("TaskTriage")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapPost("/triage", TriageAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        // A separate path segment so it can never collide with /triage above.
        group.MapPost("/{taskId:guid}/assignee-suggestions", SuggestAssigneesAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    private static async Task<IResult> TriageAsync(
        Guid boardId,
        TaskTriageRequest request,
        HttpContext http,
        ITaskTriageService triage,
        CancellationToken ct)
        => Results.Ok(await triage.TriageAsync(boardId, request, http.RequireUserId(), ct));

    private static async Task<IResult> SuggestAssigneesAsync(
        Guid boardId,
        Guid taskId,
        HttpContext http,
        ITaskTriageService triage,
        CancellationToken ct)
        => Results.Ok(await triage.SuggestAssigneesAsync(boardId, taskId, http.RequireUserId(), ct));
}
