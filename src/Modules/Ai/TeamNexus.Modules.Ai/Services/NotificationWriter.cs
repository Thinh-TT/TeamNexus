using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Board.Services;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Adapter that fulfils the Board module's <see cref="INotificationWriter"/> port (Phase 11 §6.4).
/// <para>
/// Board detects the events but must not know how an alert becomes a row; this class is the seam. It
/// delegates to <see cref="INotificationService.NotifyUsersAsync"/> so the fan-out, the recipient cap
/// and the read-state model stay in one place.
/// </para>
/// <para>
/// <b>Never throws.</b> The port contract requires it: the task or comment that triggered the alert
/// is already committed, and a notification problem must not turn a successful write into a 500.
/// </para>
/// </summary>
public sealed class NotificationWriter : INotificationWriter
{
    private readonly INotificationService _notifications;
    private readonly ILogger<NotificationWriter> _logger;

    public NotificationWriter(
        INotificationService notifications,
        ILogger<NotificationWriter> logger)
    {
        _notifications = notifications;
        _logger = logger;
    }

    public async Task NotifyAsync(MemberNotification notification, CancellationToken ct = default)
    {
        try
        {
            await _notifications.NotifyUsersAsync(
                notification.WorkspaceId,
                notification.RecipientUserIds,
                notification.Type,
                notification.Title,
                notification.Message,
                notification.PayloadJson,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not write the {Type} notification for workspace {WorkspaceId}; "
                + "the triggering request is unaffected.",
                notification.Type,
                notification.WorkspaceId);
        }
    }
}
