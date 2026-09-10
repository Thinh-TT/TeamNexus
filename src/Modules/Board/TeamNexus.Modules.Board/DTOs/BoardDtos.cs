namespace TeamNexus.Modules.Board.DTOs;

/// <summary>Create a board inside a workspace (POST /api/workspaces/{workspaceId}/boards).</summary>
public sealed record CreateBoardRequest(string Name, string? Description);

/// <summary>Full update of board metadata (PUT).</summary>
public sealed record UpdateBoardRequest(string Name, string? Description);

/// <summary>
/// Board payload. For a full-board fetch (GET by id) <see cref="Columns"/> carries each
/// column's tasks; list/create/update responses return it with empty columns.
/// </summary>
public sealed record BoardResponse(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ColumnResponse> Columns);
