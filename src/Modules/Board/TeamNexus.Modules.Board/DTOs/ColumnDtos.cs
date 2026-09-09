namespace TeamNexus.Modules.Board.DTOs;

/// <summary>Create a column; position is auto-assigned (end of the board).</summary>
public sealed record CreateColumnRequest(string Name, bool IsDone = false);

/// <summary>Partial update: null fields are left unchanged.</summary>
public sealed record UpdateColumnRequest(string? Name, bool? IsDone);

public sealed record ColumnPositionItem(Guid Id, int Position);

/// <summary>Batch reorder payload: the full set of the board's columns with new positions.</summary>
public sealed record ReorderColumnsRequest(IReadOnlyList<ColumnPositionItem> Items);

/// <summary>
/// Column payload. <see cref="Tasks"/> is populated for full-board fetches and left empty
/// for the column-list endpoint.
/// </summary>
public sealed record ColumnResponse(
    Guid Id,
    Guid BoardId,
    string Name,
    int Position,
    bool IsDone,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TaskResponse> Tasks);
