using Microsoft.EntityFrameworkCore;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// One member's capacity plus their measured current load (Giai đoạn 14.7 §A4).
/// <para>
/// Deliberately a plain record with no EF entity and no persona/email: this is the *measurement*, shared by
/// every consumer, and each one decorates it with whatever else it needs.
/// </para>
/// </summary>
public sealed record MemberWorkload(
    Guid UserId,
    string DisplayName,
    string? PrimaryRole,
    IReadOnlyList<string> Skills,
    double CapacityWeight,
    int MaxWipLimit,
    int OpenTaskCount,
    int OverdueCount,
    int InProgressCount,
    MemberType MemberType,
    string? PersonaKey);

/// <summary>
/// The single source of "how loaded is each member of this workspace" (Giai đoạn 14.7 Track B §B2).
/// <para>
/// <b>Why this exists as a service rather than as a private method.</b> §A4 (assignee dispatching) and the
/// agent tool <c>InspectMemberWorkload</c> must answer the same question. Two implementations of "open,
/// overdue, in-progress" would drift the moment either's definition of "open" was adjusted — and the drift
/// would be invisible, because each would look internally consistent. Both read this one query instead.
/// </para>
/// <para>
/// The <c>MemberType</c>/<c>PersonaKey</c> columns are carried (not filtered on) so a caller that must
/// exclude agents can, while the dispatching path — where dispatching work to an agent is the point — keeps
/// them as candidates.
/// </para>
/// </summary>
public interface IMemberWorkloadReader
{
    /// <summary>
    /// Loads up to <paramref name="maxMembers"/> actionable members of the workspace, ordered by join date
    /// (which is also the order the label sequence is built from).
    /// </summary>
    Task<IReadOnlyList<MemberWorkload>> LoadAsync(
        Guid workspaceId, int maxMembers, CancellationToken ct = default);

    /// <summary>One member's workload, or <c>null</c> when they are not a member of the workspace.</summary>
    Task<MemberWorkload?> LoadOneAsync(
        Guid workspaceId, Guid userId, CancellationToken ct = default);
}

/// <inheritdoc cref="IMemberWorkloadReader" />
public sealed class MemberWorkloadReader : IMemberWorkloadReader
{
    /// <summary>
    /// Upper bound on the task rows read to build the load picture. The load figures are advisory inputs, so a
    /// bounded page is preferable to an unbounded aggregate over a workspace with tens of thousands of cards.
    /// </summary>
    private const int LoadCandidateLimit = 500;

    private readonly TeamNexusDbContext _db;

    public MemberWorkloadReader(TeamNexusDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<MemberWorkload>> LoadAsync(
        Guid workspaceId, int maxMembers, CancellationToken ct = default)
    {
        var take = Math.Max(1, maxMembers);

        var members = await _db.WorkspaceMembers
            .AsNoTracking()
            .Where(wm => wm.WorkspaceId == workspaceId)
            .Include(wm => wm.User)
            .OrderBy(wm => wm.JoinedAt)
            .Take(take)
            .ToListAsync(ct);

        return await MeasureAsync(workspaceId, members, ct);
    }

    public async Task<MemberWorkload?> LoadOneAsync(
        Guid workspaceId, Guid userId, CancellationToken ct = default)
    {
        var members = await _db.WorkspaceMembers
            .AsNoTracking()
            .Where(wm => wm.WorkspaceId == workspaceId && wm.UserId == userId)
            .Include(wm => wm.User)
            .ToListAsync(ct);

        var measured = await MeasureAsync(workspaceId, members, ct);

        return measured.FirstOrDefault();
    }

    /// <summary>
    /// One bounded read of every open task assigned to these members, grouped in memory afterwards.
    /// <para>
    /// <b>Why in memory.</b> The "is done" test lives on the column and <c>tasks.priority</c> is text, so a
    /// per-member aggregate would be several round-trips for numbers this small; the page is capped by
    /// <see cref="LoadCandidateLimit"/> so the cost stays one query.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<MemberWorkload>> MeasureAsync(
        Guid workspaceId,
        IReadOnlyList<WorkspaceMember> members,
        CancellationToken ct)
    {
        var actionable = members.Where(wm => wm.User is not null).ToList();

        if (actionable.Count == 0)
        {
            return [];
        }

        var userIds = actionable.Select(wm => wm.UserId).ToList();
        var now = DateTimeOffset.UtcNow;

        var openTasks = await _db.Tasks
            .AsNoTracking()
            .Where(t => t.Board!.WorkspaceId == workspaceId
                        && t.AssigneeId != null
                        && userIds.Contains(t.AssigneeId.Value)
                        && (t.Column == null || !t.Column.IsDone)
                        && t.CompletedAt == null)
            .Select(t => new
            {
                AssigneeId = t.AssigneeId!.Value,
                t.DueDate,
                InProgress = t.Column != null && !t.Column.IsDone && !t.Column.IsClarification,
            })
            .Take(LoadCandidateLimit)
            .ToListAsync(ct);

        var load = openTasks
            .GroupBy(t => t.AssigneeId)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    Open = group.Count(),
                    Overdue = group.Count(t => t.DueDate.HasValue && t.DueDate.Value < now),
                    InProgress = group.Count(t => t.InProgress),
                });

        return actionable
            .Select(member =>
            {
                var stats = load.GetValueOrDefault(member.UserId);

                return new MemberWorkload(
                    member.UserId,
                    member.User!.DisplayName,
                    member.PrimaryRole,
                    member.Skills.ToList(),
                    member.CapacityWeight,
                    member.MaxWipLimit,
                    stats?.Open ?? 0,
                    stats?.Overdue ?? 0,
                    stats?.InProgress ?? 0,
                    member.MemberType,
                    member.PersonaKey);
            })
            .ToList();
    }
}
