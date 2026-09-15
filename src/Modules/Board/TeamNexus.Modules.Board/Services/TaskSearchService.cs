using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Implementation of the cross-board task search (Phase 12 §2).
/// <para>
/// <b>Ordering:</b> relevance first when a text query is present — a task whose <i>title</i> matches
/// is more useful than one that only mentions the word in its description — then
/// <c>updated_at DESC, id DESC</c>, which is also the keyset key and therefore keeps paging stable
/// while new tasks are being created.
/// </para>
/// <para>
/// <b>Why not <c>due_date</c> as the cursor key:</b> it is nullable, so ordering it means NULLS
/// LAST/FIRST semantics and a three-part cursor. <c>updated_at</c> is non-null on every row, which
/// makes the cursor two opaque parts and the comparison a plain row-value compare.
/// </para>
/// </summary>
public sealed class TaskSearchService : ITaskSearchService
{
    /// <summary>Cursor format: <c>"{updatedAt:O}|{id:D}"</c>.</summary>
    private const char CursorSeparator = '|';

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IAiAgentResolver _agents;
    private readonly TimeProvider _clock;

    public TaskSearchService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IAiAgentResolver agents,
        TimeProvider clock)
    {
        _db = db;
        _access = access;
        _agents = agents;
        _clock = clock;
    }

    public async Task<TaskSearchResponse> SearchAsync(
        TaskSearchRequest request, Guid userId, CancellationToken ct = default)
    {
        // 404 for a non-member (never 403: the workspace must not be confirmed to an outsider).
        await _access.RequireMemberAsync(request.WorkspaceId, userId, ct);

        var now = _clock.GetUtcNow();
        var hasQuery = !string.IsNullOrWhiteSpace(request.Query);
        var boardIds = await ResolveBoardScopeAsync(request, ct);
        var labelScope = await ResolveLabelScopeAsync(request, ct);
        await RequireAssigneeInWorkspaceAsync(request, ct);
        var cursor = ParseCursor(request.Cursor);

        var query = BuildQuery(request, hasQuery, now, boardIds, labelScope, cursor);

        // One extra row answers HasMore without a COUNT (same trick as WorkspaceActivityService).
        var page = await query.Take(request.Take + 1).ToListAsync(ct);

        var hasMore = page.Count > request.Take;
        var pageTasks = hasMore ? page.Take(request.Take).ToList() : page;

        var items = await MapPageAsync(pageTasks, ct);

        return new TaskSearchResponse(
            items,
            hasMore && pageTasks.Count > 0
                ? FormatCursor(pageTasks[^1].UpdatedAt, pageTasks[^1].Id)
                : null,
            hasMore,
            hasQuery);
    }

    // ---- scope resolution --------------------------------------------------

    /// <summary>
    /// Board ids the search may look at. A single <c>?boardId=</c> must belong to the workspace
    /// (otherwise 404 — "this board is not yours" and "this board does not exist" must look alike);
    /// without it the scope is every live board of the workspace.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveBoardScopeAsync(TaskSearchRequest request, CancellationToken ct)
    {
        if (request.BoardId is { } boardId)
        {
            var belongs = await _db.Boards
                .AsNoTracking()
                .AnyAsync(b => b.Id == boardId && b.WorkspaceId == request.WorkspaceId, ct);

            if (!belongs)
            {
                throw new NotFoundException("Board not found.");
            }

            return [boardId];
        }

        return await _db.Boards
            .AsNoTracking()
            .Where(b => b.WorkspaceId == request.WorkspaceId)
            .Select(b => b.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Label ids of the requested AND filter, validated against the workspace's own labels. An id
    /// from another workspace would silently return nothing — a 400 says why instead.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveLabelScopeAsync(TaskSearchRequest request, CancellationToken ct)
    {
        if (request.LabelIds.Count == 0)
        {
            return [];
        }

        var found = await _db.Labels
            .AsNoTracking()
            .Where(l => l.WorkspaceId == request.WorkspaceId && request.LabelIds.Contains(l.Id))
            .Select(l => l.Id)
            .ToListAsync(ct);

        if (found.Count != request.LabelIds.Count)
        {
            throw new BadRequestException("One or more labels do not belong to this workspace.");
        }

        return request.LabelIds;
    }

    /// <summary>
    /// A filter on <c>assigneeId</c> only means something for somebody who could hold a task in this
    /// workspace, so an id from outside it is a 400.
    /// <para>
    /// <b>Why not return an empty page:</b> no task can ever match a non-member, so the caller would
    /// get a silent "no results" for a filter that is actually invalid — indistinguishable from a typo
    /// in the search box, and impossible to debug from the UI. Same reasoning as the label check above.
    /// </para>
    /// </summary>
    private async Task RequireAssigneeInWorkspaceAsync(TaskSearchRequest request, CancellationToken ct)
    {
        if (request.Unassigned || request.AssigneeId is not { } assigneeId)
        {
            return;
        }

        var isMember = await _db.WorkspaceMembers
            .AnyAsync(wm => wm.WorkspaceId == request.WorkspaceId && wm.UserId == assigneeId, ct);

        if (!isMember)
        {
            throw new BadRequestException("Assignee is not a member of this workspace.");
        }
    }

    // ---- the query ---------------------------------------------------------
    private IQueryable<BoardTask> BuildQuery(
        TaskSearchRequest request,
        bool hasQuery,
        DateTimeOffset now,
        IReadOnlyList<Guid> boardIds,
        IReadOnlyList<Guid> labelScope,
        (DateTimeOffset UpdatedAt, Guid Id)? cursor)
    {
        var query = _db.Tasks
            .AsNoTracking()
            .Where(t => boardIds.Contains(t.BoardId));

        if (request.BoardId is { } boardId)
        {
            // boardIds is already narrowed to the one board; this keeps the intent explicit and lets
            // EF use the (board_id, column_id, position) index for the common case.
            query = query.Where(t => t.BoardId == boardId);
        }

        if (request.AssigneeId is { } assigneeId && !request.Unassigned)
        {
            query = query.Where(t => t.AssigneeId == assigneeId);
        }

        if (request.Unassigned)
        {
            query = query.Where(t => t.AssigneeId == null);
        }

        if (hasQuery)
        {
            // ILike is PostgreSQL's case-insensitive LIKE (same choice as SearchSystemDataTool, and
            // the reason this feature needs no unaccent/full-text dependency).
            var pattern = $"%{EscapeLike(request.Query!)}%";
            query = query.Where(t =>
                EF.Functions.ILike(t.Title, pattern)
                || (t.Description != null && EF.Functions.ILike(t.Description, pattern)));
        }

        if (request.Priority.Length > 0)
        {
            var priority = Enum.Parse<TaskPriority>(request.Priority, ignoreCase: true);
            query = query.Where(t => t.Priority == priority);
        }

        if (request.DueFrom is { } dueFrom)
        {
            query = query.Where(t => t.DueDate != null && t.DueDate >= dueFrom);
        }

        if (request.DueTo is { } dueTo)
        {
            query = query.Where(t => t.DueDate != null && t.DueDate <= dueTo);
        }

        if (request.Overdue)
        {
            // Matches ReportAggregator/DashboardService: open AND strictly past the due date.
            query = query.Where(t =>
                (t.Column == null || !t.Column.IsDone)
                && t.CompletedAt == null
                && t.DueDate != null
                && t.DueDate < now);
        }

        if (!request.IncludeDone)
        {
            query = query.Where(t =>
                (t.Column == null || !t.Column.IsDone)
                && t.CompletedAt == null);
        }

        if (labelScope.Count > 0)
        {
            // AND, not OR: "has label A and label B" is what a multi-select means to a user.
            // A bare non-equality join per id keeps this translatable to plain SQL without a GROUP BY.
            foreach (var labelId in labelScope)
            {
                var scoped = labelId;
                query = query.Where(t => _db.TaskLabels.Any(tl => tl.TaskId == t.Id && tl.LabelId == scoped));
            }
        }

        if (cursor is { } c)
        {
            // Row-value comparison over the keyset (see the class doc comment). Guid comparison is
            // translated by Npgsql to the server's ordering, so no client-side evaluation sneaks in.
            query = query.Where(t =>
                t.UpdatedAt < c.UpdatedAt
                || (t.UpdatedAt == c.UpdatedAt && t.Id < c.Id));
        }

        // Rank 0 = the text matched the title, rank 1 = it only matched the description. Without a
        // query every row is rank 0 and the ordering is purely by recency.
        if (!hasQuery)
        {
            return query
                .OrderByDescending(t => t.UpdatedAt)
                .ThenByDescending(t => t.Id);
        }

        var titlePattern = $"%{EscapeLike(request.Query!)}%";
        return query
            .OrderBy(t => EF.Functions.ILike(t.Title, titlePattern) ? 0 : 1)
            .ThenByDescending(t => t.UpdatedAt)
            .ThenByDescending(t => t.Id);
    }

    // ---- page materialization ----------------------------------------------

    private async Task<IReadOnlyList<TaskSearchItem>> MapPageAsync(List<BoardTask> tasks, CancellationToken ct)
    {
        if (tasks.Count == 0)
        {
            return [];
        }

        var taskIds = tasks.Select(t => t.Id).ToList();

        // The three shared batch loaders: exactly what TaskService uses for a Kanban page, so a search
        // hit shows the same labels, comment count and agent badge as the board it came from (§P1).
        var labelsByTask = await TaskReadHelpers.LoadLabelsByTaskAsync(_db, taskIds, ct);
        var counts = await TaskReadHelpers.LoadCommentCountsAsync(_db, taskIds, ct);
        var activeRunIds = await AgentRunLookup.LoadActiveRunIdsAsync(_db, taskIds, ct);

        // Workspace is a per-task fact through its board; every task in the page shares the searched
        // workspace, so the AI-agent question is asked once with that id.
        var workspaceId = await _db.Boards
            .AsNoTracking()
            .Where(b => b.Id == tasks[0].BoardId)
            .Select(b => b.WorkspaceId)
            .FirstOrDefaultAsync(ct);

        var assigneeIsAiAgent = await TaskReadHelpers.ResolveAssigneeIsAiAgentAsync(_agents, workspaceId, tasks, ct);

        var boardNames = await _db.Boards
            .AsNoTracking()
            .Where(b => taskIds.Count > 0 && tasks.Select(t => t.BoardId).Contains(b.Id))
            .Select(b => new { b.Id, b.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var columnRows = await _db.BoardColumns
            .AsNoTracking()
            .Where(c => tasks.Select(t => t.ColumnId).Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.IsDone })
            .ToDictionaryAsync(x => x.Id, x => new { x.Name, x.IsDone }, ct);

        return tasks
            .Select(t =>
            {
                var column = columnRows.GetValueOrDefault(t.ColumnId);
                var response = DtoMapping.MapTask(
                    t,
                    labelsByTask.GetValueOrDefault(t.Id, []),
                    counts.GetValueOrDefault(t.Id),
                    assigneeIsAiAgent,
                    // TryGetValue, NOT GetValueOrDefault: the latter yields Guid.Empty for a task with
                    // no live run, which serializes as "00000000-…" instead of null (Phase 7 harness bug).
                    activeRunIds.TryGetValue(t.Id, out var runId) ? runId : null);

                return new TaskSearchItem(
                    response,
                    boardNames.GetValueOrDefault(t.BoardId, string.Empty),
                    column?.Name ?? string.Empty,
                    column?.IsDone ?? false);
            })
            .ToList();
    }

    // ---- cursor ------------------------------------------------------------

    private static string FormatCursor(DateTimeOffset updatedAt, Guid id)
        => $"{updatedAt:O}{CursorSeparator}{id:D}";

    /// <summary>
    /// Parses <c>"{updatedAt:O}|{id:D}"</c>. A malformed cursor is a 400 with a clear message — never
    /// a 500, and never a silent "start from the beginning" (which would make the UI loop forever).
    /// </summary>
    private static (DateTimeOffset UpdatedAt, Guid Id)? ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        var separator = cursor.IndexOf(CursorSeparator);
        if (separator <= 0 || separator == cursor.Length - 1)
        {
            throw new BadRequestException("Invalid cursor: expected '<ISO-8601 timestamp>|<guid>'.");
        }

        var timestamp = cursor[..separator];
        var id = cursor[(separator + 1)..];

        if (!DateTimeOffset.TryParse(
                timestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var updatedAt)
            || !Guid.TryParse(id, out var taskId))
        {
            throw new BadRequestException("Invalid cursor: expected '<ISO-8601 timestamp>|<guid>'.");
        }

        return (updatedAt, taskId);
    }

    /// <summary>
    /// Escapes the two LIKE metacharacters so a user typing <c>100%</c> or <c>a_b</c> searches for
    /// those literal characters instead of matching everything. PostgreSQL treats <c>\</c> as the
    /// default escape character.
    /// </summary>
    private static string EscapeLike(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
