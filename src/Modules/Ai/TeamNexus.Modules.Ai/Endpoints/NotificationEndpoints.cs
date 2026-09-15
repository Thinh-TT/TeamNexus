using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Ai.Endpoints;

/// <summary>
/// Notification centre endpoints (Phase 5 §5.2) under <c>/api/notifications</c>.
/// <para>
/// Deliveries are per recipient, so every route is scoped to the caller: the list only returns the
/// caller's own rows and marking read can only touch the caller's own notification (anything else
/// is a 404 — a plain member must not even learn that a Manager alert exists).
/// </para>
/// </summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapGet("/", ListAsync).RequireAuthorization();

        group.MapPost("/{notificationId:guid}/read", MarkReadAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        group.MapPost("/read-all", MarkAllReadAsync)
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    // ---- handlers ----------------------------------------------------------

    private static async Task<IResult> ListAsync(
        string? isRead,
        string? kind,
        int? take,
        HttpContext http,
        INotificationService notifications,
        CancellationToken ct)
    {
        // take <= 0 falls back to the service default (20); the service clamps to 1..100.
        // kind is parsed strictly here: an unknown family must be a 400, never "no notifications".
        var result = await notifications.ListAsync(
            http.RequireUserId(), ParseIsRead(isRead), NotificationVocabulary.Parse(kind), take ?? 0, ct);

        return Results.Ok(result);
    }

    private static async Task<IResult> MarkReadAsync(
        Guid notificationId,
        HttpContext http,
        INotificationService notifications,
        CancellationToken ct)
        => Results.Ok(await notifications.MarkReadAsync(notificationId, http.RequireUserId(), ct));

    private static async Task<IResult> MarkAllReadAsync(
        HttpContext http,
        INotificationService notifications,
        CancellationToken ct)
        => Results.Ok(new { updated = await notifications.MarkAllReadAsync(http.RequireUserId(), ct) });

    // ---- helpers -----------------------------------------------------------

    /// <summary>
    /// Strict, name-only read-flag parse: any casing of true/false is accepted, anything else is a
    /// domain 400 (binding a bool directly would produce a framework ProblemDetails instead of the
    /// module's <c>{ error }</c> contract). Same spirit as the Phase 4 status filter.
    /// </summary>
    private static bool? ParseIsRead(string? isRead)
    {
        var value = isRead?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new BadRequestException($"Unknown value for isRead '{value}'. Expected true or false.");
    }
}
