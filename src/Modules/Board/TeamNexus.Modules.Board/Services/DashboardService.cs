using Microsoft.EntityFrameworkCore;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;
using TeamNexus.Shared.Risk;
using BoardEntity = TeamNexus.Persistence.Data.Entities.Board;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Builds the workspace dashboard: "Task của tôi", board summaries, workspace activity and totals
/// (Phase 12 §1, decisions D5/D6/D7).
/// <para>
/// <b>Read-only.</b> It writes nothing, owns no table and needs no migration — every number comes
/// from <c>tasks</c>, <c>board_columns</c>, <c>boards</c> and <c>activity_logs</c>.
/// </para>
/// <para>
/// <b>Why <c>TimeProvider</c> and not <c>DateTimeOffset.UtcNow</c>:</b> the three buckets are defined
/// by boundaries ("due today", "due within N days", "assigned within 7 days"), and a test that cannot
/// move the clock can only assert the middle of a range. Injecting the clock keeps the boundaries
/// testable and keeps the computation deterministic, the same spirit as <c>ReportAggregator</c>.
/// </para>
/// </summary>
public interface IDashboardService
{
    Task<DashboardResponse> GetAsync(
        Guid workspaceId,
        Guid userId,
        int? dueSoonDays = null,
        int? take = null,
        CancellationToken ct = default);
}

public sealed class DashboardService : IDashboardService
{
    /// <summary>"Sắp đến hạn" window when the caller does not ask for one.</summary>
    public const int DefaultDueSoonDays = 3;

    /// <summary>Upper bound on the window — beyond a couple of weeks nothing is "coming up" any more.</summary>
    public const int MaxDueSoonDays = 30;

    /// <summary>Rows per bucket when the caller does not ask for a number.</summary>
    public const int DefaultTake = 10;

    /// <summary>Upper bound per bucket: the dashboard is an overview, not a task list.</summary>
    public const int MaxTake = 50;

    /// <summary>How far back "mới giao" reaches (a fixed product rule, not a knob).</summary>
    public const int RecentlyAssignedDays = 7;

    /// <summary>Boards rendered in the summary block; a workspace with more than this is truncated.</summary>
    public const int MaxBoards = 20;

    /// <summary>Rows in the recent-activity feed.</summary>
    public const int MaxActivities = 10;

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly TimeProvider _clock;

    public DashboardService(TeamNexusDbContext db, IWorkspaceAccess access, TimeProvider clock)
    {
        _db = db;
        _access = access;
        _clock = clock;
    }

    public async Task<DashboardResponse> GetAsync(
        Guid workspaceId,
        Guid userId,
        int? dueSoonDays = null,
        int? take = null,
        CancellationToken ct = default)
    {
        // 404 for a non-member — the dashboard must not reveal that a workspace exists (D5).
        await _access.RequireMemberAsync(workspaceId, userId, ct);

        var now = _clock.GetUtcNow();
        var days = Clamp(dueSoonDays, DefaultDueSoonDays, 1, MaxDueSoonDays);
        var pageSize = Clamp(take, DefaultTake, 1, MaxTake);

        var workspaceName = await _db.Workspaces
            .AsNoTracking()
            .Where(w => w.Id == workspaceId)
            .Select(w => w.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var boards = await _db.Boards
            .AsNoTracking()
            .Where(b => b.WorkspaceId == workspaceId)
            .OrderBy(b => b.CreatedAt)
            .Select(b => new BoardRow(b.Id, b.Name))
            .ToListAsync(ct);

        var boardIds = boards.Select(b => b.Id).ToList();

        // tasks carries the filter that hides rows whose own OR whose board's deleted_at is set
        // (BoardTaskConfiguration), so soft-deleted boards simply do not contribute.
        var tasks = boardIds.Count == 0
            ? []
            : await _db.Tasks
                .AsNoTracking()
                .Where(t => boardIds.Contains(t.BoardId))
                .Select(t => new TaskRow(
                    t.Id,
                    t.BoardId,
                    t.ColumnId,
                    t.Title,
                    t.DueDate,
                    t.Priority,
                    t.CreatedAt,
                    t.UpdatedAt,
                    t.AssigneeId,
                    t.CompletedAt))
                .ToListAsync(ct);

        var columns = boardIds.Count == 0
            ? []
            : await _db.BoardColumns
                .AsNoTracking()
                .Where(c => boardIds.Contains(c.BoardId))
                .OrderBy(c => c.Position)
                .Select(c => new ColumnRow(c.Id, c.BoardId, c.Name, c.IsDone, c.Position))
                .ToListAsync(ct);

        var isDoneColumn = columns.Where(c => c.IsDone).Select(c => c.Id).ToHashSet();
        var columnNameById = columns.ToDictionary(c => c.Id, c => c.Name);
        var boardNameById = boards.ToDictionary(b => b.Id, b => b.Name);

        var myTasks = BuildMyTasks(
            tasks ?? [], userId, now, days, pageSize, boardNameById, columnNameById, isDoneColumn);

        var visibleBoards = boards.Take(MaxBoards).ToList();
        var summaries = BuildBoardSummaries(visibleBoards, tasks ?? [], columns, isDoneColumn, now);
        var activities = await LoadActivitiesAsync(workspaceId, ct);

        var summary = new DashboardSummary(
            TotalTasks: tasks?.Count ?? 0,
            DoneTasks: tasks?.Count(t => IsDone(t, isDoneColumn)) ?? 0,
            OpenTasks: tasks?.Count(t => !IsDone(t, isDoneColumn)) ?? 0,
            OverdueTasks: tasks?.Count(t => IsOverdue(t, isDoneColumn, now)) ?? 0,
            MyOpenTasks: tasks?.Count(t => t.AssigneeId == userId && !IsDone(t, isDoneColumn)) ?? 0);

        // Phase 14 §3.2: the health verdict is computed from the SAME rows the summary above was
        // computed from, so the gauge can never contradict the KPI cards next to it. No extra query.
        var health = DashboardProjectHealth.From(
            ProjectHealth.Compute(BuildHealthInput(tasks ?? [], isDoneColumn, now)));

        return new DashboardResponse(
            workspaceId,
            workspaceName,
            now,
            days,
            myTasks,
            summaries,
            boards.Count > MaxBoards,
            activities,
            summary)
        {
            Health = health,
        };
    }

    // ---- project health (Phase 14 §3.2) -------------------------------------

    /// <summary>
    /// Turns the already-loaded rows into the counts <see cref="ProjectHealth"/> needs.
    /// <para>
    /// <c>AtRiskTasks</c> goes through <c>DeadlineRiskRules</c> — the <b>same</b> shared rule the AI
    /// Observer uses for its <c>AtRiskDeadline</c> alert — so "2 thẻ sắp hết hạn" on the gauge and the
    /// Observer's alert can never describe different tasks. <c>LastActivityAt</c> mirrors
    /// <c>ObserverSignalDetector.ActivityAt</c>: a comment newer than the last edit counts as movement.
    /// </para>
    /// </summary>
    private static ProjectHealthInput BuildHealthInput(
        IReadOnlyList<TaskRow> tasks,
        IReadOnlySet<Guid> isDoneColumn,
        DateTimeOffset now)
    {
        var open = tasks.Where(t => !IsDone(t, isDoneColumn)).ToList();
        var stalledThreshold = TimeSpan.FromDays(ProjectHealth.DefaultStalledDays);

        var stalled = open.Count(t => now - LastActivityAt(t) > stalledThreshold);

        var oldestAgeDays = open.Count == 0
            ? 0
            : (int)Math.Max(0, Math.Floor(open.Max(t => (now - t.CreatedAt).TotalDays)));

        var byAssignee = open
            .Where(t => t.AssigneeId.HasValue)
            .GroupBy(t => t.AssigneeId!.Value)
            .Select(g => g.Count())
            .ToList();

        return new ProjectHealthInput(
            TotalTasks: tasks.Count,
            OpenTasks: open.Count,
            OverdueTasks: open.Count(t => IsOverdue(t, isDoneColumn, now)),
            AtRiskTasks: open.Count(t => DeadlineRiskRules.IsAtRiskDeadline(ToWorkItem(t, now), now)),
            StalledTasks: stalled,
            OldestOpenTaskAgeDays: oldestAgeDays,
            MaxOpenTasksPerAssignee: byAssignee.Count == 0 ? 0 : byAssignee.Max(),
            AssigneeCount: byAssignee.Count);
    }

    /// <summary>
    /// Projection for the shared risk rule. The dashboard carries no comment timestamps, so
    /// <c>updated_at</c> alone is the movement signal here — a narrower window than the Observer's
    /// (which also sees comments), never a wider one. See §R6 of the phase report.
    /// </summary>
    private static ProjectWorkItem ToWorkItem(TaskRow task, DateTimeOffset now)
        => new(task.Id, task.CreatedAt, task.DueDate, LastActivityAt(task));

    /// <summary>Last movement: the later of creation and the last edit (never a future instant).</summary>
    private static DateTimeOffset LastActivityAt(TaskRow task)
        => task.UpdatedAt > task.CreatedAt ? task.UpdatedAt : task.CreatedAt;

    // ---- "Task của tôi" -----------------------------------------------------

    private static DashboardMyTasks BuildMyTasks(
        IReadOnlyList<TaskRow> tasks,
        Guid userId,
        DateTimeOffset now,
        int days,
        int pageSize,
        IReadOnlyDictionary<Guid, string> boardNameById,
        IReadOnlyDictionary<Guid, string> columnNameById,
        IReadOnlySet<Guid> isDoneColumn)
    {
        var mine = tasks.Where(t => t.AssigneeId == userId).ToList();
        var dueSoonEnd = now.AddDays(days);

        // Overdue is computed through IsOverdue/OverdueByDays so the boundary ("due exactly now is
        // NOT overdue") has a single definition shared with `summary.OverdueTasks`.
        var overdue = mine
            .Where(t => IsOverdue(t, isDoneColumn, now))
            .OrderBy(t => t.DueDate)
            .ThenBy(t => t.Title, StringComparer.Ordinal)
            .ThenBy(t => t.Id)
            .ToList();

        // `dueSoon` deliberately EXCLUDES overdue tasks: the three tabs are an urgency partition
        // ("đã trễ" / "sắp tới hạn" / "vừa được giao"), and a task the server just called overdue
        // showing up again under "sắp đến hạn" reads like the dashboard contradicts itself.
        var dueSoon = mine
            .Where(t => !IsDone(t, isDoneColumn)
                        && !IsOverdue(t, isDoneColumn, now)
                        && t.DueDate is { } due
                        && due >= now
                        && due <= dueSoonEnd)
            .OrderBy(t => t.DueDate)
            .ThenBy(t => t.Title, StringComparer.Ordinal)
            .ThenBy(t => t.Id)
            .ToList();

        var recentlyAssigned = mine
            .Where(t => !IsDone(t, isDoneColumn)
                        && t.CreatedAt >= now.AddDays(-RecentlyAssignedDays))
            .OrderByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .ToList();

        return new DashboardMyTasks(
            Bucket(overdue, pageSize, boardNameById, columnNameById, isDoneColumn, now),
            Bucket(dueSoon, pageSize, boardNameById, columnNameById, isDoneColumn, now),
            Bucket(recentlyAssigned, pageSize, boardNameById, columnNameById, isDoneColumn, now));
    }

    private static DashboardTaskBucket Bucket(
        IReadOnlyList<TaskRow> ordered,
        int pageSize,
        IReadOnlyDictionary<Guid, string> boardNameById,
        IReadOnlyDictionary<Guid, string> columnNameById,
        IReadOnlySet<Guid> isDoneColumn,
        DateTimeOffset now)
        => new(
            ordered.Count,
            ordered.Take(pageSize).Select(t => ToItem(t, boardNameById, columnNameById, isDoneColumn, now)).ToList());

    private static DashboardTaskItem ToItem(
        TaskRow task,
        IReadOnlyDictionary<Guid, string> boardNameById,
        IReadOnlyDictionary<Guid, string> columnNameById,
        IReadOnlySet<Guid> isDoneColumn,
        DateTimeOffset now)
        => new(
            task.Id,
            task.BoardId,
            task.ColumnId,
            task.Title,
            boardNameById.GetValueOrDefault(task.BoardId, string.Empty),
            columnNameById.GetValueOrDefault(task.ColumnId, string.Empty),
            task.DueDate,
            task.Priority?.ToString(),
            task.CreatedAt,
            task.AssigneeId,
            IsDone(task, isDoneColumn),
            OverdueByDays(task, isDoneColumn, now));

    // ---- board summaries ----------------------------------------------------

    private static List<DashboardBoardSummary> BuildBoardSummaries(
        IReadOnlyList<BoardRow> boards,
        IReadOnlyList<TaskRow> tasks,
        IReadOnlyList<ColumnRow> columns,
        IReadOnlySet<Guid> isDoneColumn,
        DateTimeOffset now)
    {
        var tasksByBoard = tasks.GroupBy(t => t.BoardId).ToDictionary(g => g.Key, g => g.ToList());
        var columnsByBoard = columns.GroupBy(c => c.BoardId).ToDictionary(g => g.Key, g => g.ToList());

        return boards
            .Select(board =>
            {
                // Every board gets a row, even with zero tasks: a workspace overview that silently
                // drops an empty board reads like the board was deleted.
                var boardTasks = tasksByBoard.GetValueOrDefault(board.Id, []);
                var boardColumns = columnsByBoard.GetValueOrDefault(board.Id, []);
                var countByColumn = boardTasks
                    .GroupBy(t => t.ColumnId)
                    .ToDictionary(g => g.Key, g => g.Count());

                var done = boardTasks.Count(t => IsDone(t, isDoneColumn));

                return new DashboardBoardSummary(
                    board.Id,
                    board.Name,
                    Total: boardTasks.Count,
                    Done: done,
                    Open: boardTasks.Count - done,
                    Overdue: boardTasks.Count(t => IsOverdue(t, isDoneColumn, now)),
                    Columns: boardColumns
                        .Select(c => new DashboardBoardColumnCount(
                            c.Id,
                            c.Name,
                            c.IsDone,
                            countByColumn.GetValueOrDefault(c.Id, 0)))
                        .ToList());
            })
            .ToList();
    }

    // ---- recent activity ----------------------------------------------------

    /// <summary>
    /// Newest 10 events of the workspace, with the actor's display name via a LEFT JOIN.
    /// <c>ActivityLog</c> declares its FKs without navigation properties, so <c>Include</c> is not
    /// available here — the same reason <c>WorkspaceActivityService</c> uses <c>LeftJoin</c>.
    /// <para>
    /// This is the <b>member-level</b> view: unlike the Manager-only activity page it returns no
    /// payload (P5), just enough to render "ai đã làm gì, khi nào".
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<DashboardActivityItem>> LoadActivitiesAsync(
        Guid workspaceId,
        CancellationToken ct)
    {
        var rows = await _db.Activities
            .AsNoTracking()
            .Where(a => a.WorkspaceId == workspaceId)
            .LeftJoin(
                _db.Users,
                a => a.UserId,
                u => (Guid?)u.Id,
                (a, u) => new
                {
                    a.Id,
                    a.BoardId,
                    a.EntityType,
                    a.Action,
                    AuthorName = u == null ? null : u.DisplayName,
                    a.CreatedAt,
                })
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(MaxActivities)
            .ToListAsync(ct);

        return rows
            .Select(x => new DashboardActivityItem(
                x.Id,
                x.BoardId,
                x.EntityType,
                x.Action,
                x.AuthorName,
                x.CreatedAt,
                // Always null — see P5. Serializing the jsonb back for every member would widen the
                // data surface far beyond what the feed needs.
                null))
            .ToList();
    }

    // ---- shared definitions (must match ReportAggregator) -------------------

    /// <summary>
    /// "Done" = sitting in an <c>is_done</c> column OR carrying <c>completed_at</c>. Identical to
    /// <c>ReportAggregator.IsDone</c>; if this drifts, the dashboard and the exported report will
    /// quote different completion numbers for the same workspace (Phase 6 §1.3).
    /// </summary>
    private static bool IsDone(TaskRow task, IReadOnlySet<Guid> isDoneColumn)
        => isDoneColumn.Contains(task.ColumnId) || task.CompletedAt.HasValue;

    /// <summary>
    /// A task is overdue when it is <b>not done</b> and its due date is strictly in the past.
    /// <c>dueDate == now</c> is NOT overdue — matching <c>ReportAggregator.IsOverdue</c> and
    /// <c>ObserverSignalDetector</c>, so the Observer never flags a task the dashboard calls on time.
    /// </summary>
    private static bool IsOverdue(TaskRow task, IReadOnlySet<Guid> isDoneColumn, DateTimeOffset now)
        => !IsDone(task, isDoneColumn)
           && task.DueDate is { } due
           && due < now;

    private static int? OverdueByDays(TaskRow task, IReadOnlySet<Guid> isDoneColumn, DateTimeOffset now)
    {
        if (!IsOverdue(task, isDoneColumn, now))
        {
            return null;
        }

        // Whole days elapsed, minimum 1: a task one hour past its due date reads as "Quá hạn 1 ngày"
        // rather than "0 ngày", which would look like "not late".
        var elapsed = now - task.DueDate!.Value;
        return Math.Max(1, (int)elapsed.TotalDays);
    }

    /// <summary>
    /// Clamps a UI tuning knob into range. An out-of-range number is clamped rather than rejected
    /// (same rule as <c>WorkspaceActivityService.ClampTake</c>) — <c>days=0</c> becomes 1, <c>days=99</c>
    /// becomes <c>max</c>; a value the model binder could not parse at all never reaches here, and the
    /// endpoint answers 400 for that.
    /// <para>
    /// Written with explicit comparisons rather than relational patterns in a <c>switch</c>: a pattern
    /// arm such as <c>&gt; max</c> requires <c>max</c> to be a compile-time constant (CS9135), which a
    /// per-call bound cannot be.
    /// </para>
    /// </summary>
    private static int Clamp(int? value, int fallback, int min, int max)
    {
        if (value is not { } requested)
        {
            return fallback;
        }

        if (requested < min)
        {
            return min;
        }

        return requested > max ? max : requested;
    }

    // ---- projections --------------------------------------------------------

    private sealed record BoardRow(Guid Id, string Name);

    private sealed record ColumnRow(Guid Id, Guid BoardId, string Name, bool IsDone, int Position);

    private sealed record TaskRow(
        Guid Id,
        Guid BoardId,
        Guid ColumnId,
        string Title,
        DateTimeOffset? DueDate,
        TaskPriority? Priority,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        Guid? AssigneeId,
        DateTimeOffset? CompletedAt);
}
