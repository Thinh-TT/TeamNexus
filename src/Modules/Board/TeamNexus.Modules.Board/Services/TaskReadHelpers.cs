using Microsoft.EntityFrameworkCore;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// The three batch loaders every "read a page of tasks" path needs in order to fill a
/// <see cref="TaskResponse"/> without N+1 queries (Phase 12 §P1).
/// <para>
/// Extracted verbatim from <see cref="TaskService"/> when <see cref="TaskSearchService"/> arrived:
/// a search hit renders exactly like a Kanban card, so it needs the same labels, the same comment
/// count and the same <c>assigneeIsAiAgent</c> flag. Copying ~40 lines of query code — or worse,
/// resolving those values per task — would create two places to keep in sync. The SQL is
/// deliberately unchanged; this is an extract-method refactor, not a rewrite.
/// </para>
/// </summary>
internal static class TaskReadHelpers
{
    /// <summary>
    /// Labels of each task in <paramref name="taskIds"/>, grouped by task. One query for the whole
    /// page; tasks without labels are absent from the result (callers use
    /// <c>GetValueOrDefault(id, [])</c>).
    /// </summary>
    public static async Task<Dictionary<Guid, IReadOnlyList<LabelResponse>>> LoadLabelsByTaskAsync(
        TeamNexusDbContext db, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default)
    {
        if (taskIds.Count == 0)
        {
            return [];
        }

        var links = await db.TaskLabels
            .Where(tl => taskIds.Contains(tl.TaskId))
            .Include(tl => tl.Label)
            .AsNoTracking()
            .ToListAsync(ct);

        return links
            .Where(tl => tl.Label is not null)
            .GroupBy(tl => tl.TaskId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<LabelResponse>)g.Select(tl => DtoMapping.MapLabel(tl.Label!)).ToList());
    }

    /// <summary>Comment count of each task in <paramref name="taskIds"/>, one grouped query.</summary>
    public static async Task<Dictionary<Guid, int>> LoadCommentCountsAsync(
        TeamNexusDbContext db, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default)
    {
        if (taskIds.Count == 0)
        {
            return [];
        }

        return await db.TaskComments
            .Where(c => taskIds.Contains(c.TaskId))
            .GroupBy(c => c.TaskId)
            .Select(g => new { TaskId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TaskId, x => x.Count, ct);
    }

    /// <summary>
    /// <c>AssigneeIsAiAgent</c> for a whole page (Phase 7 §3.3). There is exactly ONE agent per
    /// workspace (partial unique index <c>uq_workspace_members_ai_agent</c>), so the resolver is
    /// asked once per distinct assignee in the page rather than once per task. If a workspace ever
    /// gets several agents this must become a set-based lookup.
    /// </summary>
    public static async Task<bool> ResolveAssigneeIsAiAgentAsync(
        IAiAgentResolver agents,
        Guid workspaceId,
        IReadOnlyCollection<BoardTask> tasks,
        CancellationToken ct = default)
    {
        foreach (var assigneeId in tasks
                     .Where(t => t.AssigneeId.HasValue)
                     .Select(t => t.AssigneeId!.Value)
                     .Distinct())
        {
            if (await agents.IsAiAgentAsync(workspaceId, assigneeId, ct))
            {
                return true;
            }
        }

        return false;
    }
}
