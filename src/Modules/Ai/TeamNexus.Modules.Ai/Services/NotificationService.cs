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
/// One non-Observer alert to fan out to the workspace's Managers/Admins (Phase 7 §4.8d).
/// <paramref name="PayloadJson"/> is the already-serialized jsonb payload.
/// </summary>
public sealed record AgentNotification(string Type, string Title, string Message, string PayloadJson);

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

    /// <summary>
    /// Fan-out of ONE agent alert to the same Manager/Admin recipient set (Phase 7 §4.8d).
    /// No deduplication window: the callers are already idempotent per event
    /// (<c>notification_sent</c> / one alert per run), so a dedupe key would only suppress the
    /// second genuine failure of a re-run.
    /// </summary>
    Task<int> NotifyManagersAsync(
        Guid workspaceId,
        AgentNotification notification,
        CancellationToken ct = default);

    /// <summary>
    /// Notifications of one recipient, newest first, optionally filtered by read state and by alert
    /// family (Phase 12 §3.4).
    /// </summary>
    /// <param name="kind">
    /// The type whitelist to restrict to, or <c>null</c> for every kind (the <c>kind</c> query
    /// parameter was absent). Callers build it through <see cref="NotificationVocabulary.Parse"/>.
    /// The unread count is computed over the <b>same</b> filter, so the badge always explains the list.
    /// </param>
    Task<NotificationListResponse> ListAsync(
        Guid userId, bool? isRead, IReadOnlyList<string>? kind, int take, CancellationToken ct = default);

    /// <summary>Marks one notification read; 404 when it is not this recipient's (idempotent).</summary>
    Task<NotificationResponse> MarkReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default);

    /// <summary>Marks every unread notification of the recipient read; returns rows affected.</summary>
    Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Total unread notifications of the recipient (badge count).</summary>
    Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Fan-out of ONE alert to an <b>explicit</b> set of recipients — the non-Observer path
    /// (Phase 11 §6.2): task assignments and comments on a task someone is responsible for.
    /// <para>
    /// No deduplication window: the callers are already idempotent per event (a task is assigned
    /// once, a comment is written once), so a dedupe key could only swallow a genuine second alert.
    /// Duplicate ids and the acting user are handled by the caller, not here.
    /// </para>
    /// </summary>
    /// <param name="recipientUserIds">
    /// Empty ⇒ nothing is written (logged, never thrown): the caller's write has already committed.
    /// </param>
    Task<int> NotifyUsersAsync(
        Guid workspaceId,
        IReadOnlyCollection<Guid> recipientUserIds,
        string type,
        string title,
        string message,
        string? payloadJson = null,
        CancellationToken ct = default);
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

    // ---- agent alerts (Phase 7 §4.8d) --------------------------------------

    /// <summary>
    /// One alert per Manager/Admin of the workspace, reusing the Observer's recipient loader so the
    /// cap, the deterministic ordering and the "agent is only a Member" exclusion all stay in one
    /// place. A workspace without a Manager is logged, never thrown: the agent's run must not fail
    /// because nobody could be told about it.
    /// </summary>
    public async Task<int> NotifyManagersAsync(
        Guid workspaceId,
        AgentNotification notification,
        CancellationToken ct = default)
    {
        var recipients = await LoadManagerIdsAsync(workspaceId, ct);
        if (recipients.Count == 0)
        {
            _logger.LogWarning(
                "Agent notification {Type} could not be delivered: workspace {WorkspaceId} has no Manager/Admin.",
                notification.Type,
                workspaceId);

            return 0;
        }

        var rows = recipients
            .Select(userId => new Notification
            {
                WorkspaceId = workspaceId,
                RecipientUserId = userId,
                Type = notification.Type,
                Title = notification.Title,
                Message = notification.Message,
                Payload = notification.PayloadJson,
            })
            .ToList();

        _db.Notifications.AddRange(rows);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Agent notification {Type} written for {Recipients} recipient(s) of workspace {WorkspaceId}.",
            notification.Type,
            rows.Count,
            workspaceId);

        return rows.Count;
    }

    public async Task<NotificationListResponse> ListAsync(
        Guid userId, bool? isRead, IReadOnlyList<string>? kind, int take, CancellationToken ct = default)
    {
        var limit = take <= 0 ? DefaultListTake : Math.Clamp(take, 1, MaxListTake);

        var query = _db.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientUserId == userId);

        if (isRead.HasValue)
        {
            query = query.Where(n => n.IsRead == isRead.Value);
        }

        if (kind is { Count: > 0 })
        {
            // Filtered in SQL, not in memory: the count below must be computed over exactly the rows the
            // list can return, which a client-side filter cannot guarantee.
            query = query.Where(n => kind.Contains(n.Type));
        }

        var rows = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        // Unread count is a total, not the count within the page (badge semantics) — and it honours the
        // same type filter, so `kind=observer&isRead=false` answers "how many Observer alerts are
        // waiting for me", which is what the dashboard tile needs.
        var unreadQuery = _db.Notifications
            .Where(n => n.RecipientUserId == userId && !n.IsRead);

        if (kind is { Count: > 0 })
        {
            unreadQuery = unreadQuery.Where(n => kind.Contains(n.Type));
        }

        var unread = await unreadQuery.CountAsync(ct);

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

    // ---- user-facing alerts (Phase 11 §6.2) --------------------------------

    /// <summary>
    /// Recipient cap for one user-facing alert. The Observer cap is bound to
    /// <c>Observer:MaxManagersPerWorkspace</c>, which is named for a different question ("how many
    /// managers can we bother?") — reusing it here would make a task assignment silently stop being
    /// delivered when someone tunes the Observer.
    /// </summary>
    public const int MaxRecipientsPerNotification = 100;

    public async Task<int> NotifyUsersAsync(
        Guid workspaceId,
        IReadOnlyCollection<Guid> recipientUserIds,
        string type,
        string title,
        string message,
        string? payloadJson = null,
        CancellationToken ct = default)
    {
        var recipients = (recipientUserIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(MaxRecipientsPerNotification)
            .ToList();

        if (recipients.Count == 0)
        {
            return 0;
        }

        var rows = recipients
            .Select(userId => new Notification
            {
                WorkspaceId = workspaceId,
                RecipientUserId = userId,
                Type = type,
                Title = Truncate(title, 200),
                Message = Truncate(message, 2000),
                Payload = payloadJson,
            })
            .ToList();

        _db.Notifications.AddRange(rows);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Notification {Type} written for {Recipients} recipient(s) of workspace {WorkspaceId}.",
            type,
            rows.Count,
            workspaceId);

        return rows.Count;
    }

    /// <summary>
    /// Clamps a value to a column limit. The callers pass user content (a task title, a comment
    /// excerpt), so an over-long value must be trimmed rather than turning a successful write into a
    /// database error — and the comment excerpt is a preview, never the full text.
    /// </summary>
    public static string Truncate(string? value, int max)
    {
        var text = value ?? string.Empty;
        return text.Length <= max ? text : text[..max];
    }

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
