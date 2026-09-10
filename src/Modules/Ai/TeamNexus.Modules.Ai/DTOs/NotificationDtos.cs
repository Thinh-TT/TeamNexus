using System.Text.Json;

namespace TeamNexus.Modules.Ai.DTOs;

/// <summary>
/// One alert as returned by the notification endpoints (Phase 5 §5.1).
/// <para>
/// <see cref="Payload"/> stays a raw <see cref="JsonElement"/> on purpose: it is jsonb produced by
/// <c>ObserverNotificationFactory</c>, and a malformed/legacy value must degrade to <c>null</c>
/// rather than fail the request (same defensive convention as the Phase 4 snapshots).
/// </para>
/// </summary>
public sealed record NotificationResponse(
    Guid Id,
    Guid WorkspaceId,
    string Type,
    string Title,
    string Message,
    JsonElement? Payload,
    bool IsRead,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

/// <summary>
/// Notification page plus the caller's total unread count. <see cref="UnreadCount"/> is a total,
/// not a count within <see cref="Items"/> — it drives the badge and must not change with <c>take</c>.
/// </summary>
public sealed record NotificationListResponse(int UnreadCount, IReadOnlyList<NotificationResponse> Items);
