namespace TeamNexus.Modules.Board.DTOs;

/// <summary>Create a column; position is auto-assigned (end of the board).</summary>
/// <param name="IsClarification">
/// Phase 7 §3.5 — mark the column as the "Chờ làm rõ" lane. Mutually exclusive with
/// <paramref name="IsDone"/> (400 when both are true). Null/false by default.
/// </param>
public sealed record CreateColumnRequest(string Name, bool IsDone = false, bool? IsClarification = false);

/// <summary>Partial update: null fields are left unchanged.</summary>
public sealed record UpdateColumnRequest(string? Name, bool? IsDone, bool? IsClarification = null);

public sealed record ColumnPositionItem(Guid Id, int Position);

/// <summary>Batch reorder payload: the full set of the board's columns with new positions.</summary>
public sealed record ReorderColumnsRequest(IReadOnlyList<ColumnPositionItem> Items);

/// <summary>
/// Column payload. <see cref="Tasks"/> is populated for full-board fetches and left empty
/// for the column-list endpoint.
/// </summary>
/// <param name="IsClarification">
/// Phase 7 §3.5 — the "Chờ làm rõ" column managed by the AI Agent: at most one per board, never
/// treated as "done", and it cannot be deleted.
/// </param>
public sealed record ColumnResponse(
    Guid Id,
    Guid BoardId,
    string Name,
    int Position,
    bool IsDone,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TaskResponse> Tasks,
    bool IsClarification);
