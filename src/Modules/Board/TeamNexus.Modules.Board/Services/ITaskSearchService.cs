using TeamNexus.Modules.Board.DTOs;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// A parsed, validated cross-board task search (Phase 12 §2).
/// <para>
/// The endpoint parses the query string into this record and calls
/// <see cref="ITaskSearchService.SearchAsync"/>; keeping the raw query-string names out of the
/// service means the validation rules ("<c>labelIds</c> is a CSV of at most 10 ids", "<c>unassigned</c>
/// beats <c>assigneeId</c>") live in one place and the service reads like a query, not like a parser.
/// </para>
/// <para>
/// Public (not internal) because it appears in the signature of the public
/// <see cref="ITaskSearchService"/> — an internal parameter type on a public method is a compiler
/// error, and making the whole service internal would break DI registration from <c>Program.cs</c>.
/// </para>
/// <param name="WorkspaceId">Scope of the search. The caller's membership is checked by the service.</param>
/// <param name="Query">Free text matched against <c>title</c> and <c>description</c>; null/blank means "no text filter".</param>
/// <param name="BoardId">Restrict to one board of the workspace; a board from another workspace is a 404.</param>
/// <param name="AssigneeId">Restrict to one assignee, who must be a member of the workspace.</param>
/// <param name="Unassigned">When true, only tasks with no assignee (wins over <see cref="AssigneeId"/>).</param>
/// <param name="LabelIds">Tasks must carry <b>all</b> of these labels (AND, not OR).</param>
/// <param name="Priority">One of Low/Medium/High/Urgent (names only — a numeric string is a 400).</param>
/// <param name="DueFrom">Inclusive lower bound on <c>due_date</c>, already normalized to UTC.</param>
/// <param name="DueTo">Inclusive upper bound on <c>due_date</c>, already normalized to UTC.</param>
/// <param name="Overdue">When true, only tasks that are open and past their due date.</param>
/// <param name="IncludeDone">False excludes tasks in a done column or with <c>completed_at</c>. Defaults to true.</param>
/// <param name="Take">Page size, already clamped by the endpoint.</param>
/// <param name="Cursor">Keyset cursor of the previous page, or null for the first page.</param>
public sealed record TaskSearchRequest(
    Guid WorkspaceId,
    string? Query,
    Guid? BoardId,
    Guid? AssigneeId,
    bool Unassigned,
    IReadOnlyList<Guid> LabelIds,
    string Priority,
    DateTimeOffset? DueFrom,
    DateTimeOffset? DueTo,
    bool Overdue,
    bool IncludeDone,
    int Take,
    string? Cursor);

/// <summary>
/// Cross-board task search for the workspace search page (Phase 12 §2, decisions D8/D9/D10).
/// <para>
/// <b>Why a new endpoint instead of extending <c>GET /api/boards/{boardId}/tasks</c>:</b> that route
/// returns a bare <c>TaskResponse[]</c> and is parsed by <c>useBoard</c>, <c>boardStore</c> and
/// kanban drag-and-drop. Giving it a paged envelope — or a scope beyond one board — would break a
/// verified contract; a search page needs both.
/// </para>
/// <para>
/// All filters are applied in SQL. Labels/comments/agent-run are then resolved for the page only, in
/// grouped queries (<see cref="TaskReadHelpers"/>), exactly like the Kanban board does.
/// </para>
/// </summary>
public interface ITaskSearchService
{
    Task<TaskSearchResponse> SearchAsync(TaskSearchRequest request, Guid userId, CancellationToken ct = default);
}
