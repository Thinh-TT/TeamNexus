using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Builds the daily digest from the workspace dashboards the recipient belongs to (Phase 13 §3.2).
/// <para>
/// <b>Read-only.</b> It writes no table, emits no in-app notification, appends nothing to
/// <c>activity_logs</c> and never goes near the Accountability Layer — a digest is a scheduled re-read of
/// data the user can already see, so it must not become an event source of its own. (A test asserts the
/// absence of those writes.)
/// </para>
/// <para>
/// <b>Why it reuses <see cref="IDashboardService"/>:</b> "task của tôi", the board summaries and the
/// workspace totals are already defined in exactly one place, together with the shared
/// <c>IsDone</c>/<c>IsOverdue</c> rules. Assembling a second query here is how the email and the web page
/// start quoting different numbers for the same workspace.
/// </para>
/// </summary>
public sealed class DailyDigestService : IDailyDigestService
{
    /// <summary>
    /// Fallback for <c>Frontend:BaseUrl</c> when the section is missing — the same default the Board and
    /// Ai modules already use for invitation links, so a misconfigured link behaves identically everywhere.
    /// </summary>
    private const string DefaultFrontendBaseUrl = "http://localhost:5173";

    private readonly TeamNexusDbContext _db;
    private readonly IDashboardService _dashboard;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<DailyDigestService> _logger;

    public DailyDigestService(
        TeamNexusDbContext db,
        IDashboardService dashboard,
        ILogger<DailyDigestService> logger,
        IConfiguration? configuration = null)
    {
        _db = db;
        _dashboard = dashboard;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<DailyDigestContent?> BuildAsync(
        Guid userId,
        DateOnly todayLocal,
        int maxWorkspaces,
        int maxItemsPerBucket,
        CancellationToken ct = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.DisplayName, u.DigestEnabled })
            .FirstOrDefaultAsync(ct);

        // No account, no address (an AI Agent pseudo-member has email = null), or opted out ⇒ nothing to do.
        // All three are ordinary states, not errors.
        if (user is null || !user.DigestEnabled || string.IsNullOrWhiteSpace(user.Email))
        {
            return null;
        }

        var workspaces = await _db.WorkspaceMembers
            .AsNoTracking()
            // No explicit `DeletedAt` check: the WorkspaceMember global query filter already hides
            // memberships of a soft-deleted workspace (the same rule WorkspaceMemberService relies on).
            .Where(wm => wm.UserId == userId && wm.Workspace != null)
            .OrderBy(wm => wm.JoinedAt)
            .ThenBy(wm => wm.WorkspaceId)
            .Take(Math.Max(1, maxWorkspaces))
            .Select(wm => new { wm.WorkspaceId, wm.Workspace!.Name })
            .ToListAsync(ct);

        var sections = new List<DailyDigestWorkspaceSection>();
        var firstWorkspaceId = (Guid?)null;

        foreach (var workspace in workspaces)
        {
            DashboardResponse? dashboard;

            try
            {
                dashboard = await _dashboard.GetAsync(workspace.WorkspaceId, userId, null, maxItemsPerBucket, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One unreadable workspace must not cancel everybody else's digest.
                _logger.LogWarning(
                    ex,
                    "Bỏ qua workspace {WorkspaceId} khi dựng digest cho {UserId}.",
                    workspace.WorkspaceId,
                    userId);

                continue;
            }

            // A workspace where the user has nothing open is noise, so it is left out entirely.
            if (!HasAnythingToReport(dashboard))
            {
                continue;
            }

            firstWorkspaceId ??= workspace.WorkspaceId;
            sections.Add(new DailyDigestWorkspaceSection(dashboard));
        }

        if (sections.Count == 0)
        {
            return null;
        }

        var baseUrl = ResolveFrontendBaseUrl();
        var dashboardUrl = $"{baseUrl}/workspaces/{firstWorkspaceId:D}/dashboard";

        // The unsubscribe link points at the profile's "Thông báo" tab (Phase 13 §3.2 ô C). A signed-out
        // user still lands on the login page with `next=/profile`, so this stays useful for everyone.
        var unsubscribeUrl = $"{baseUrl}/profile#notifications";

        return new DailyDigestContent(
            user.Email!,
            user.DisplayName,
            todayLocal,
            sections,
            dashboardUrl,
            unsubscribeUrl);
    }

    /// <summary>
    /// True when the recipient has at least one open task somewhere in this workspace.
    /// <para>
    /// "Open" is read from <c>summary.myOpenTasks</c>, which <see cref="DashboardService"/> derives from the
    /// same <c>IsDone</c> rule as everything else — so the digest can never disagree with the dashboard
    /// about whether a person has work to do.
    /// </para>
    /// </summary>
    private static bool HasAnythingToReport(DashboardResponse dashboard)
        => dashboard.Summary.MyOpenTasks > 0;

    private string ResolveFrontendBaseUrl()
    {
        var configured = _configuration?.GetSection("Frontend:BaseUrl").Value;

        return string.IsNullOrWhiteSpace(configured)
            ? DefaultFrontendBaseUrl
            : configured.TrimEnd('/');
    }
}
