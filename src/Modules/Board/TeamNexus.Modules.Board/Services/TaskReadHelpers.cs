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

    /// <summary>
    /// External GitHub PR / Issue links of each task in <paramref name="taskIds"/>, grouped by task
    /// (Phase 15.1 §T15.1.5). Ordered newest-updated first per task.
    /// </summary>
    public static async Task<Dictionary<Guid, IReadOnlyList<TaskExternalLinkSummaryResponse>>> LoadExternalLinksByTaskAsync(
        TeamNexusDbContext db, IReadOnlyCollection<Guid> taskIds, CancellationToken ct = default)
    {
        if (taskIds.Count == 0)
        {
            return [];
        }

        var links = await db.TaskExternalLinks
            .Where(l => taskIds.Contains(l.TaskId))
            .OrderByDescending(l => l.UpdatedAt)
            .ThenByDescending(l => l.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);

        return links
            .GroupBy(l => l.TaskId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<TaskExternalLinkSummaryResponse>)g.Select(DtoMapping.MapExternalLink).ToList());
    }

    /// <summary>
    /// Atomically allocates the next sequential <c>task_number</c> for <paramref name="workspaceId"/>
    /// (<c>TN-1, TN-2, ...</c>; Phase 15.1 §T15.1.5) and queues the companion <see cref="TaskNumberEntry"/> row.
    /// </summary>
    public static async Task<long> AllocateNextTaskNumberAsync(
        TeamNexusDbContext db,
        Guid workspaceId,
        Guid taskId,
        CancellationToken ct = default)
    {
        // Note: Do NOT call IQueryable.SingleAsync(ct) on SqlQueryRaw("UPDATE ... RETURNING"), because EF Core
        // wraps composable operators in `SELECT ... FROM (UPDATE ...) LIMIT 2` and throws InvalidOperationException
        // ("'FromSql' or 'SqlQuery' was called with non-composable SQL"). Materializing with ToListAsync(ct)
        // executes the raw UPDATE ... RETURNING statement directly without subquery composition.
        var allocatedRows = await db.Database
            .SqlQueryRaw<long>(
                """
                UPDATE workspaces
                SET next_task_number = next_task_number + 1
                WHERE id = {0}
                RETURNING next_task_number - 1 AS "Value"
                """,
                workspaceId)
            .ToListAsync(ct);

        var allocated = allocatedRows.Single();

        // If the Workspace entity happens to be tracked in the current DbContext scope, sync its
        // in-memory NextTaskNumber so a subsequent SaveChangesAsync never overwrites the DB value.
        var trackedWorkspace = db.ChangeTracker.Entries<Workspace>()
            .FirstOrDefault(e => e.Entity.Id == workspaceId);
        if (trackedWorkspace is not null)
        {
            trackedWorkspace.Entity.NextTaskNumber = allocated + 1;
            trackedWorkspace.Property(w => w.NextTaskNumber).IsModified = false;
        }

        db.TaskNumbers.Add(new TaskNumberEntry
        {
            WorkspaceId = workspaceId,
            TaskNumber = allocated,
            TaskId = taskId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        return allocated;
    }
}
