using Microsoft.EntityFrameworkCore;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Shared lookup for <c>TaskResponse.ActiveAgentRunId</c> (Phase 7 §3.3): both
/// <see cref="TaskService"/> and <see cref="BoardService"/> must resolve it, and missing it in one
/// of them is a silent bug (the Kanban card simply never shows the agent badge).
/// </summary>
internal static class AgentRunLookup
{
    /// <summary>
    /// Maps taskId → id of the task's newest <b>live</b> agent run, where "live" means Running,
    /// AwaitingClarification or AwaitingApproval. Tasks without a live run are absent from the
    /// result (callers use <c>GetValueOrDefault</c> → null).
    /// <para>
    /// One grouped query for the whole page (never N+1). When a task has several live runs the
    /// newest <c>Running</c> one wins; if none is running, the newest waiting one wins — that is the
    /// run the UI should be looking at.
    /// </para>
    /// </summary>
    public static async Task<Dictionary<Guid, Guid>> LoadActiveRunIdsAsync(
        TeamNexusDbContext db,
        IReadOnlyCollection<Guid> taskIds,
        CancellationToken ct = default)
    {
        if (taskIds.Count == 0)
        {
            return [];
        }

        var runs = await db.AgentRuns
            .AsNoTracking()
            .Where(r => taskIds.Contains(r.TaskId)
                        && (r.Status == AgentRunStatus.Running
                            || r.Status == AgentRunStatus.AwaitingClarification
                            || r.Status == AgentRunStatus.AwaitingApproval))
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new { r.TaskId, r.Id, r.Status })
            .ToListAsync(ct);

        return runs
            .GroupBy(r => r.TaskId)
            .ToDictionary(
                g => g.Key,
                g => (g.FirstOrDefault(r => r.Status == AgentRunStatus.Running) ?? g.First()).Id);
    }
}
