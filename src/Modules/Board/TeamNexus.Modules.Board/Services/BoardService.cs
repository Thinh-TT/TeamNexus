using Microsoft.EntityFrameworkCore;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;
using BoardEntity = TeamNexus.Persistence.Data.Entities.Board;

namespace TeamNexus.Modules.Board.Services;

public interface IBoardService
{
    Task<IReadOnlyList<BoardResponse>> GetBoardsAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);

    Task<BoardResponse> GetBoardByIdAsync(Guid boardId, Guid userId, CancellationToken ct = default);

    Task<BoardResponse> CreateBoardAsync(Guid workspaceId, CreateBoardRequest request, Guid userId, CancellationToken ct = default);

    Task<BoardResponse> UpdateBoardAsync(Guid boardId, UpdateBoardRequest request, Guid userId, CancellationToken ct = default);

    Task DeleteBoardAsync(Guid boardId, Guid userId, CancellationToken ct = default);
}

public sealed class BoardService : IBoardService
{
    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IAiAgentResolver _agents;

    public BoardService(TeamNexusDbContext db, IWorkspaceAccess access, IAiAgentResolver agents)
    {
        _db = db;
        _access = access;
        _agents = agents;
    }

    public async Task<IReadOnlyList<BoardResponse>> GetBoardsAsync(
        Guid workspaceId, Guid userId, CancellationToken ct = default)
    {
        await _access.RequireMemberAsync(workspaceId, userId, ct);

        var boards = await _db.Boards
            .Where(b => b.WorkspaceId == workspaceId)
            .OrderBy(b => b.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);

        return boards.Select(b => ToResponse(b, [])).ToList();
    }

    public async Task<BoardResponse> GetBoardByIdAsync(
        Guid boardId, Guid userId, CancellationToken ct = default)
    {
        var board = await RequireVisibleBoardAsync(boardId, userId, ct);

        var columns = await _db.BoardColumns
            .Where(c => c.BoardId == boardId)
            .OrderBy(c => c.Position)
            .AsNoTracking()
            .ToListAsync(ct);

        var tasks = await LoadTasksAsync(boardId, ct);
        var taskIds = tasks.Select(t => t.Id).ToList();
        var commentCounts = await LoadCommentCountsAsync(taskIds, ct);
        var labelsByTask = await LoadLabelsByTaskAsync(taskIds, ct);

        // Phase 7 §3.3: the Kanban board is one of the THREE task-returning paths, so the new
        // TaskResponse fields must be resolved here too — otherwise cards silently lose the agent
        // badge (regression guarded by harness check I-2).
        var activeRunIds = await AgentRunLookup.LoadActiveRunIdsAsync(_db, taskIds, ct);
        var assigneeIsAiAgent = await ResolvePageAssigneeIsAiAgentAsync(board.WorkspaceId, tasks, ct);

        var columnResponses = columns.Select(c => new ColumnResponse(
            c.Id, c.BoardId, c.Name, c.Position, c.IsDone, c.CreatedAt, c.UpdatedAt,
            tasks.Where(t => t.ColumnId == c.Id)
                 .Select(t => DtoMapping.MapTask(
                     t,
                     labelsByTask.GetValueOrDefault(t.Id, []),
                     commentCounts.GetValueOrDefault(t.Id),
                     assigneeIsAiAgent,
                     // TryGetValue, NOT GetValueOrDefault (Guid.Empty would leak as a fake run id).
                     activeRunIds.TryGetValue(t.Id, out var runId) ? runId : null))
                 .ToList(),
            c.IsClarification)).ToList();

        return ToResponse(board, columnResponses);
    }

    public async Task<BoardResponse> CreateBoardAsync(
        Guid workspaceId, CreateBoardRequest request, Guid userId, CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, userId, ct);
        ValidateBoardName(request.Name);

        var board = new BoardEntity
        {
            WorkspaceId = workspaceId,
            Name = request.Name.Trim(),
            Description = TrimToNull(request.Description),
        };

        _db.Boards.Add(board);
        await _db.SaveChangesAsync(ct);

        return ToResponse(board, []);
    }

    public async Task<BoardResponse> UpdateBoardAsync(
        Guid boardId, UpdateBoardRequest request, Guid userId, CancellationToken ct = default)
    {
        var board = await RequireVisibleBoardAsync(boardId, userId, ct);
        await _access.RequireManagerAsync(board.WorkspaceId, userId, ct);
        ValidateBoardName(request.Name);

        board.Name = request.Name.Trim();
        board.Description = TrimToNull(request.Description);

        await _db.SaveChangesAsync(ct);
        return ToResponse(board, []);
    }

    public async Task DeleteBoardAsync(Guid boardId, Guid userId, CancellationToken ct = default)
    {
        var board = await RequireVisibleBoardAsync(boardId, userId, ct);
        await _access.RequireManagerAsync(board.WorkspaceId, userId, ct);

        board.DeletedAt = DateTimeOffset.UtcNow;
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

    private async Task<List<BoardTask>> LoadTasksAsync(Guid boardId, CancellationToken ct)
        => await _db.Tasks
            .Where(t => t.BoardId == boardId)
            .Include(t => t.Assignee)
            .OrderBy(t => t.Position)
            .ThenBy(t => t.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);

    private async Task<Dictionary<Guid, IReadOnlyList<LabelResponse>>> LoadLabelsByTaskAsync(
        List<Guid> taskIds, CancellationToken ct)
    {
        if (taskIds.Count == 0)
        {
            return [];
        }

        var links = await _db.TaskLabels
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

    private async Task<Dictionary<Guid, int>> LoadCommentCountsAsync(List<Guid> taskIds, CancellationToken ct)
    {
        if (taskIds.Count == 0)
        {
            return [];
        }

        return await _db.TaskComments
            .Where(c => taskIds.Contains(c.TaskId))
            .GroupBy(c => c.TaskId)
            .Select(g => new { TaskId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TaskId, x => x.Count, ct);
    }

    private static BoardResponse ToResponse(BoardEntity board, IReadOnlyList<ColumnResponse> columns)
        => new(board.Id, board.WorkspaceId, board.Name, board.Description, board.CreatedAt, board.UpdatedAt, columns);

    private static void ValidateBoardName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
        {
            throw new BadRequestException("Board name must be 1–120 characters.");
        }
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Phase 7 §3.3 — <c>AssigneeIsAiAgent</c> for a whole board. Exactly one agent per workspace
    /// (partial unique index <c>uq_workspace_members_ai_agent</c>), so the resolver is asked once per
    /// distinct assignee, never once per task.
    /// </summary>
    private async Task<bool> ResolvePageAssigneeIsAiAgentAsync(
        Guid workspaceId, IReadOnlyList<BoardTask> tasks, CancellationToken ct)
    {
        foreach (var assigneeId in tasks
                     .Where(t => t.AssigneeId.HasValue)
                     .Select(t => t.AssigneeId!.Value)
                     .Distinct())
        {
            if (await _agents.IsAiAgentAsync(workspaceId, assigneeId, ct))
            {
                return true;
            }
        }

        return false;
    }
}
