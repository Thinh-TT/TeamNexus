using Microsoft.EntityFrameworkCore;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;
using BoardEntity = TeamNexus.Persistence.Data.Entities.Board;

namespace TeamNexus.Modules.Board.Services;

public interface IColumnService
{
    Task<IReadOnlyList<ColumnResponse>> GetColumnsAsync(Guid boardId, Guid userId, CancellationToken ct = default);

    Task<ColumnResponse> CreateColumnAsync(Guid boardId, CreateColumnRequest request, Guid userId, CancellationToken ct = default);

    Task<ColumnResponse> UpdateColumnAsync(Guid columnId, UpdateColumnRequest request, Guid userId, CancellationToken ct = default);

    Task ReorderColumnsAsync(Guid boardId, ReorderColumnsRequest request, Guid userId, CancellationToken ct = default);

    Task DeleteColumnAsync(Guid columnId, Guid userId, CancellationToken ct = default);
}

public sealed class ColumnService : IColumnService
{
    private const int ReorderOffset = 1_000_000;

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;

    public ColumnService(TeamNexusDbContext db, IWorkspaceAccess access)
    {
        _db = db;
        _access = access;
    }

    public async Task<IReadOnlyList<ColumnResponse>> GetColumnsAsync(
        Guid boardId, Guid userId, CancellationToken ct = default)
    {
        var board = await RequireVisibleBoardAsync(boardId, userId, ct);

        var columns = await _db.BoardColumns
            .Where(c => c.BoardId == boardId)
            .OrderBy(c => c.Position)
            .AsNoTracking()
            .ToListAsync(ct);

        return columns.Select(c => ToResponse(c, [])).ToList();
    }

    public async Task<ColumnResponse> CreateColumnAsync(
        Guid boardId, CreateColumnRequest request, Guid userId, CancellationToken ct = default)
    {
        var board = await RequireVisibleBoardAsync(boardId, userId, ct);
        await _access.RequireManagerAsync(board.WorkspaceId, userId, ct);
        ValidateName(request.Name);

        var maxPosition = await _db.BoardColumns
            .Where(c => c.BoardId == boardId)
            .MaxAsync(c => (int?)c.Position, ct) ?? -1;

        var column = new BoardColumn
        {
            BoardId = boardId,
            Name = request.Name.Trim(),
            Position = maxPosition + 1,
            IsDone = request.IsDone,
        };

        _db.BoardColumns.Add(column);
        await _db.SaveChangesAsync(ct);

        return ToResponse(column, []);
    }

    public async Task<ColumnResponse> UpdateColumnAsync(
        Guid columnId, UpdateColumnRequest request, Guid userId, CancellationToken ct = default)
    {
        var column = await RequireVisibleColumnAsync(columnId, userId, ct);
        await _access.RequireManagerAsync(column.Board!.WorkspaceId, userId, ct);

        if (request.Name is not null)
        {
            ValidateName(request.Name);
            column.Name = request.Name.Trim();
        }

        if (request.IsDone.HasValue)
        {
            column.IsDone = request.IsDone.Value;
        }

        await _db.SaveChangesAsync(ct);
        return ToResponse(column, []);
    }

    public async Task ReorderColumnsAsync(
        Guid boardId, ReorderColumnsRequest request, Guid userId, CancellationToken ct = default)
    {
        var board = await RequireVisibleBoardAsync(boardId, userId, ct);
        await _access.RequireManagerAsync(board.WorkspaceId, userId, ct);

        var columns = await _db.BoardColumns
            .Where(c => c.BoardId == boardId)
            .ToListAsync(ct);

        ValidateReorder(columns, request.Items);

        // Two-phase update inside one transaction: first move every column to a
        // guaranteed-unique temporary position, then apply the final positions.
        // Avoids transient violations of UQ (board_id, position) while reordering.
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        foreach (var column in columns)
        {
            column.Position -= ReorderOffset;
        }

        await _db.SaveChangesAsync(ct);

        var itemsById = request.Items.ToDictionary(i => i.Id);
        foreach (var column in columns)
        {
            column.Position = itemsById[column.Id].Position;
        }

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task DeleteColumnAsync(Guid columnId, Guid userId, CancellationToken ct = default)
    {
        var column = await RequireVisibleColumnAsync(columnId, userId, ct);
        await _access.RequireManagerAsync(column.Board!.WorkspaceId, userId, ct);

        // Tasks keep a Restrict FK to the column — including soft-deleted ones — so a
        // column that ever contained a task cannot be physically removed. Refuse with a
        // clear message instead of failing at the DB (phase-2 §2.3 / DB design §7).
        var hasTasks = await _db.Tasks
            .IgnoreQueryFilters()
            .AnyAsync(t => t.ColumnId == columnId, ct);

        if (hasTasks)
        {
            throw new ConflictException(
                "Column still has tasks (including deleted ones). Move or remove them first.");
        }

        _db.BoardColumns.Remove(column);
        await _db.SaveChangesAsync(ct);
    }

    // ---- helpers ----------------------------------------------------------

    private async Task<BoardEntity> RequireVisibleBoardAsync(Guid boardId, Guid userId, CancellationToken ct)
    {
        var board = await _db.Boards
            .FirstOrDefaultAsync(b => b.Id == boardId, ct)
            ?? throw new NotFoundException("Board not found.");

        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);
        return board;
    }

    private async Task<BoardColumn> RequireVisibleColumnAsync(Guid columnId, Guid userId, CancellationToken ct)
    {
        var column = await _db.BoardColumns
            .Include(c => c.Board)
            .FirstOrDefaultAsync(c => c.Id == columnId, ct)
            ?? throw new NotFoundException("Column not found.");

        await _access.RequireMemberAsync(column.Board!.WorkspaceId, userId, ct);
        return column;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
        {
            throw new BadRequestException("Column name must be 1–120 characters.");
        }
    }

    private static void ValidateReorder(List<BoardColumn> columns, IReadOnlyList<ColumnPositionItem> items)
    {
        if (items.Count != columns.Count)
        {
            throw new BadRequestException("Reorder must include every column of the board.");
        }

        var ids = columns.Select(c => c.Id).ToHashSet();
        var seen = new HashSet<Guid>();
        var positions = new HashSet<int>();

        foreach (var item in items)
        {
            if (!ids.Contains(item.Id) || !seen.Add(item.Id))
            {
                throw new BadRequestException("Reorder contains unknown or duplicate column ids.");
            }

            if (item.Position < 0 || !positions.Add(item.Position))
            {
                throw new BadRequestException("Reorder positions must be distinct and non-negative.");
            }
        }
    }

    private static ColumnResponse ToResponse(BoardColumn column, IReadOnlyList<TaskResponse> tasks)
        => new(column.Id, column.BoardId, column.Name, column.Position, column.IsDone,
            column.CreatedAt, column.UpdatedAt, tasks);
}
