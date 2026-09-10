using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Identity of one Observer run, carried into notification payloads so an alert can be traced
/// back to the scan that produced it (Phase 5 §4.4).
/// </summary>
public sealed record ObserverRunContext(
    Guid RunId,
    string Model,
    int? PromptTokens,
    int? CompletionTokens);

/// <summary>
/// Writes and reads the Manager-facing alerts produced by the AI Observer (Phase 5 §4.4).
/// <para>
/// Notifications are the Observer's <b>only</b> output: one row per recipient (so read state is
/// per person) and addressed to Manager/Admin of the workspace only — the API never exposes them
/// to plain members.
/// </para>
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Creates one notification per (finding, recipient) pair for the workspace's Managers/Admins,
    /// skipping alerts already created inside the deduplication window. Returns how many rows were
    /// written (0 when the workspace has no manager — logged, never thrown).
    /// </summary>
    Task<int> NotifyManagersAsync(
        Guid workspaceId,
        IReadOnlyList<ObserverFinding> findings,
        ObserverRunContext context,
        CancellationToken ct = default);

    /// <summary>Notifications of one recipient, newest first, optionally filtered by read state.</summary>
    Task<NotificationListResponse> ListAsync(
        Guid userId, bool? isRead, int take, CancellationToken ct = default);

    /// <summary>Marks one notification read; 404 when it is not this recipient's (idempotent).</summary>
    Task<NotificationResponse> MarkReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default);

    /// <summary>Marks every unread notification of the recipient read; returns rows affected.</summary>
    Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Total unread notifications of the recipient (badge count).</summary>
    Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default);
}

public sealed class NotificationService : INotificationService
{
    public const int DefaultListTake = 20;

    public const int MaxListTake = 100;

    private readonly TeamNexusDbContext _db;
    private readonly ObserverOptions _options;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        TeamNexusDbContext db,
        IOptions<ObserverOptions> options,
        ILogger<NotificationService> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> NotifyManagersAsync(
        Guid workspaceId,
        IReadOnlyList<ObserverFinding> findings,
        ObserverRunContext context,
        CancellationToken ct = default)
    {
        if (findings.Count == 0)
        {
            return 0;
        }

        var recipients = await LoadManagerIdsAsync(workspaceId, ct);
        if (recipients.Count == 0)
        {
            _logger.LogWarning(
                "Observer found {Findings} finding(s) in workspace {WorkspaceId} but it has no Manager/Admin to notify.",
                findings.Count,
                workspaceId);
            return 0;
        }

        var maxPerRun = Math.Max(1, _options.MaxNotificationsPerRun);
        var recent = await LoadRecentDedupeStateAsync(workspaceId, ct);

        var toAdd = new List<Notification>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var finding in findings)
        {
            var dedupeKey = ObserverNotificationFactory.BuildDedupeKey(finding);

            // A previously created alert for the same alert content in the window wins.
            if (recent.Contains(dedupeKey) || !seenKeys.Add(dedupeKey))
            {
                continue;
            }

            var payload = ObserverNotificationFactory.BuildPayload(finding, context);

            foreach (var userId in recipients)
            {
                if (toAdd.Count >= maxPerRun)
                {
                    _logger.LogWarning(
                        "Observer notification cap ({Cap}) reached for workspace {WorkspaceId}; remaining alerts dropped.",
                        maxPerRun,
                        workspaceId);
                    break;
                }

                toAdd.Add(new Notification
                {
                    WorkspaceId = workspaceId,
                    RecipientUserId = userId,
                    Type = finding.Type,
                    Title = finding.Title,
                    Message = finding.Message,
                    Payload = payload,
                });
            }
        }

        if (toAdd.Count == 0)
        {
            return 0;
        }

        _db.Notifications.AddRange(toAdd);
        await _db.SaveChangesAsync(ct);
        return toAdd.Count;
    }

    public async Task<NotificationListResponse> ListAsync(
        Guid userId, bool? isRead, int take, CancellationToken ct = default)
    {
        var limit = take <= 0 ? DefaultListTake : Math.Clamp(take, 1, MaxListTake);

        var query = _db.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientUserId == userId);

        if (isRead.HasValue)
        {
            query = query.Where(n => n.IsRead == isRead.Value);
        }

        var rows = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        // Unread count is a total, not the count within the page (badge semantics).
        var unread = await _db.Notifications
            .CountAsync(n => n.RecipientUserId == userId && !n.IsRead, ct);

        return new NotificationListResponse(unread, rows.Select(ToResponse).ToList());
    }

    public async Task<NotificationResponse> MarkReadAsync(
        Guid notificationId, Guid userId, CancellationToken ct = default)
    {
        // A notification of another recipient must look like it does not exist.
        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientUserId == userId, ct)
            ?? throw new NotFoundException("Notification not found.");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return ToResponse(notification);
    }

    public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct = default)
        => await _db.Notifications
            .Where(n => n.RecipientUserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(n => n.IsRead, true)
                    .SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow),
                ct);

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default)
        => _db.Notifications.CountAsync(n => n.RecipientUserId == userId && !n.IsRead, ct);

    // ---- helpers -----------------------------------------------------------

    /// <summary>
    /// Manager/Admin recipients, capped and ordered by user id so the same workspace always
    /// produces the same recipient set (deterministic, verify-friendly).
    /// </summary>
    private async Task<IReadOnlyList<Guid>> LoadManagerIdsAsync(Guid workspaceId, CancellationToken ct)
    {
        var cap = Math.Max(1, _options.MaxManagersPerWorkspace);

        // WorkspaceMember's global query filter already hides soft-deleted workspaces.
        return await _db.WorkspaceMembers
            .AsNoTracking()
            .Where(wm => wm.WorkspaceId == workspaceId
                         && (wm.Role == WorkspaceRole.Manager || wm.Role == WorkspaceRole.Admin))
            .OrderBy(wm => wm.UserId)
            .Take(cap)
            .Select(wm => wm.UserId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Dedup keys of notifications already created inside the window, for any recipient of this
    /// workspace. Compared semantically (jsonb normalizes key order/whitespace — Phase 4 §1.2).
    /// </summary>
    private async Task<HashSet<string>> LoadRecentDedupeStateAsync(Guid workspaceId, CancellationToken ct)
    {
        var windowStart = DateTimeOffset.UtcNow.AddHours(-Math.Max(1, _options.DeduplicationWindowHours));

        var recent = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.WorkspaceId == workspaceId && n.CreatedAt >= windowStart)
            .Select(n => new { n.Type, n.Payload })
            .ToListAsync(ct);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in recent)
        {
            keys.Add(ObserverNotificationFactory.BuildDedupeKey(row.Type, row.Payload));
        }

        return keys;
    }

    private static NotificationResponse ToResponse(Notification notification)
        => new(
            notification.Id,
            notification.WorkspaceId,
            notification.Type,
            notification.Title,
            notification.Message,
            AiActionService.ParseJson(notification.Payload),
            notification.IsRead,
            notification.CreatedAt,
            notification.ReadAt);
}
