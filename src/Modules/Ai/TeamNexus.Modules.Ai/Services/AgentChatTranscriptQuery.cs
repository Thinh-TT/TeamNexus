using Microsoft.EntityFrameworkCore;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// The one read rule for a stored chat transcript (Phase 14.6 §A3).
/// <para>
/// <b>Why the ordering is not just <c>created_at</c>.</b> Both rows of an exchange are written in a single
/// <c>SaveChangesAsync</c>, and the audit stamping takes <b>one</b> timestamp per save
/// (<c>TeamNexusDbContext.StampAuditableTimestamps</c>: <c>var now = DateTimeOffset.UtcNow;</c>). The question
/// and its answer therefore carry the <b>identical</b> <c>created_at</c>, and the previous
/// <c>ThenByDescending(Id)</c> tie-break decided the visible order with a random Guid — so roughly half of all
/// reloads showed the answer above the question it answers. Found by
/// <c>Phase146TrackATests.PHASE146A_A1</c>; the board transcript from Phase 14.5 had the same latent defect.
/// </para>
/// <para>
/// The role is what actually distinguishes the pair, and a conversation always reads question-then-answer, so
/// that is the second sort key. The id stays as a final tie-break purely so the order is stable — an
/// identifier may not be chronological, but it is deterministic.
/// </para>
/// </summary>
internal static class AgentChatTranscriptQuery
{
    /// <summary>Page size when a caller does not ask for one.</summary>
    public const int DefaultTake = 50;

    /// <summary>Hard ceiling on a page, so one request cannot pull a whole history.</summary>
    public const int MaxTake = 200;

    /// <summary>
    /// The newest <paramref name="take"/> turns of an already-scoped query, returned <b>oldest first</b> so the
    /// UI can render a conversation top-down.
    /// </summary>
    /// <param name="scoped">
    /// The caller's query, already filtered to one board (<c>board_id</c>) or one task (<c>task_id</c>). The
    /// two scopes stay in their own services because they are genuinely different questions; only the paging
    /// is shared.
    /// </param>
    public static async Task<List<AgentChatMessage>> NewestPageAsync(
        IQueryable<AgentChatMessage> scoped,
        DateTimeOffset? before,
        int take,
        CancellationToken ct)
    {
        var query = scoped.AsNoTracking();

        if (before is { } cursor)
        {
            query = query.Where(m => m.CreatedAt < cursor);
        }

        var rows = await query
            .OrderByDescending(m => m.CreatedAt)
            // The tie-break that actually decides an exchange's order — see the remarks above. These are the
            // NEWEST-first keys, and the page is reversed below; so the answer sorts before its question HERE,
            // which is what puts the question first in reading order.
            .ThenByDescending(m => m.Role == AgentChatRole.Assistant)
            .ThenByDescending(m => m.Id)
            .Take(ClampTake(take))
            .ToListAsync(ct);

        rows.Reverse();

        return rows;
    }

    /// <summary>Clamps a caller-supplied page size into <c>1..MaxTake</c>, with 0/negative meaning "default".</summary>
    public static int ClampTake(int take) => take <= 0 ? DefaultTake : Math.Clamp(take, 1, MaxTake);
}
