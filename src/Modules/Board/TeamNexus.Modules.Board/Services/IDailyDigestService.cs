using TeamNexus.Modules.Board.DTOs;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// One workspace's slice of a user's daily digest (Phase 13 §3.2).
/// <para>
/// <b>Deliberately reuses <see cref="DashboardResponse"/></b> rather than re-projecting the numbers into a
/// second shape: the digest is "the dashboard, delivered by email", and two parallel projections would
/// drift the day one of them learns a new rule. The one field the dashboard does not carry is the
/// recipient, so that lives on <see cref="DailyDigestContent"/>.
/// </para>
/// </summary>
/// <param name="Dashboard">Exactly what <c>GET /api/workspaces/{id}/dashboard</c> would have returned.</param>
public sealed record DailyDigestWorkspaceSection(DashboardResponse Dashboard);

/// <summary>
/// Everything the digest email needs, assembled by the Board module (Phase 13 §3.2, decision D14).
/// <para>
/// The Ai module owns the wording, the schedule and the transport; Board owns the data and knows how to
/// read it. This record is the seam, and it carries only already-computed values plus two links — so the
/// renderer is a pure function over it and needs neither a clock nor a database.
/// </para>
/// </summary>
/// <param name="RecipientEmail">Where the digest goes. Never logged in full by the runner.</param>
/// <param name="RecipientDisplayName">Used to greet the reader; may be empty.</param>
/// <param name="SendDateLocal">
/// The date this digest represents, <b>in the recipient's timezone</b> — the subject line's date and the
/// "hôm nay" the text refers to must be the reader's day, not the server's UTC day.
/// </param>
/// <param name="Workspaces">
/// Sections, ordered by the recipient's <c>joined_at</c> so the digest is stable across runs. Never empty:
/// a user with nothing to report gets no email at all (see the service).
/// </param>
/// <param name="DashboardUrl">Deep link to the first workspace's dashboard.</param>
/// <param name="UnsubscribeUrl">Deep link to the profile's notification tab (Phase 13 §3.2 ô C).</param>
public sealed record DailyDigestContent(
    string RecipientEmail,
    string RecipientDisplayName,
    DateOnly SendDateLocal,
    IReadOnlyList<DailyDigestWorkspaceSection> Workspaces,
    string DashboardUrl,
    string UnsubscribeUrl);

/// <summary>
/// Assembles a user's daily digest for the Ai module (Phase 13 §3.2, decision D14).
/// <para>
/// The port lives in <b>Board</b> because Board owns <c>tasks</c>, <c>board_columns</c>, <c>boards</c>,
/// <c>workspace_members</c> and the <c>dashboard</c> assembly — the Ai module already references Board, so
/// Board declaring the contract is the only direction that keeps the dependency arrow pointing one way
/// (the same reasoning as <c>IActivityLogWriter</c>, <c>IAiAgentResolver</c> and <c>INotificationWriter</c>).
/// </para>
/// <para>
/// <b>Implementations must never throw.</b> A digest is a best-effort broadcast: one unreadable workspace
/// must not cancel everybody else's email (same fail-soft rule as <c>IEmailGateway</c>).
/// </para>
/// </summary>
public interface IDailyDigestService
{
    /// <summary>
    /// Builds the digest for one user, or <c>null</c> when there is nothing worth sending.
    /// <para>
    /// Returns <c>null</c> — and the caller then sends nothing — when the user has opted out, has no
    /// e-mail address (an AI Agent pseudo-member), or has no open task anywhere. An empty digest email is
    /// worse than no email: it trains people to ignore the one that matters.
    /// </para>
    /// </summary>
    /// <param name="userId">The recipient. Nothing about another user's tasks is ever read.</param>
    /// <param name="todayLocal">Today's date in the recipient's timezone (the runner computes it).</param>
    /// <param name="maxWorkspaces">Upper bound on sections, so one busy member cannot generate a huge mail.</param>
    /// <param name="maxItemsPerBucket">Upper bound on task rows per bucket (totals stay exact).</param>
    Task<DailyDigestContent?> BuildAsync(
        Guid userId,
        DateOnly todayLocal,
        int maxWorkspaces,
        int maxItemsPerBucket,
        CancellationToken ct = default);
}

/// <summary>
/// No-op implementation used when the Ai module is not registered, and as the Board module's own default.
/// <para>
/// It reports "nothing to send" rather than throwing, so a harness that only loads Board keeps working and
/// an unconfigured digest is silently absent instead of crashing a hosted service.
/// </para>
/// </summary>
public sealed class NullDailyDigestService : IDailyDigestService
{
    public Task<DailyDigestContent?> BuildAsync(
        Guid userId,
        DateOnly todayLocal,
        int maxWorkspaces,
        int maxItemsPerBucket,
        CancellationToken ct = default)
        => Task.FromResult<DailyDigestContent?>(null);
}
