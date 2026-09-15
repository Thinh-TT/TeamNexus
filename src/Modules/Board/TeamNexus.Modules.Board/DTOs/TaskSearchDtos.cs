namespace TeamNexus.Modules.Board.DTOs;

/// <summary>
/// One search hit: the standard task payload plus the board/column context a cross-board result list
/// needs (Phase 12 §2, decision P4).
/// <para>
/// <see cref="Task"/> is the <b>unchanged</b> <see cref="TaskResponse"/>, so a result card can be
/// rendered with exactly the fields the Kanban card already consumes. The extra context stays on the
/// envelope instead of being appended to a contract that four existing clients parse.
/// </para>
/// </summary>
/// <param name="IsDoneColumn">
/// True when the task's column is flagged <c>is_done</c>. The result list is not inside a board, so it
/// has no column object to read the flag from, and "đã xong" must not depend on <c>completedAt</c>
/// alone (older rows in a done column have no <c>completed_at</c>).
/// </param>
public sealed record TaskSearchItem(
    TaskResponse Task,
    string BoardName,
    string ColumnName,
    bool IsDoneColumn);

/// <summary>
/// One page of search results.
/// </summary>
/// <param name="NextCursor">
/// Opaque cursor for the next page, or null on the last page. Keyset over
/// <c>(updated_at DESC, id DESC)</c>: stable while new tasks are created, and it uses an existing
/// index instead of the <c>OFFSET</c> scan a growing task table would punish.
/// </param>
/// <param name="HasQuery">
/// False when the request carried no usable text query. The UI must say "nhập từ khoá để tìm"
/// rather than "không tìm thấy kết quả" — two empty states that mean different things.
/// </param>
public sealed record TaskSearchResponse(
    IReadOnlyList<TaskSearchItem> Items,
    string? NextCursor,
    bool HasMore,
    bool HasQuery);
