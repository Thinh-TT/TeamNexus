using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;

namespace TeamNexus.Modules.Board.Hubs;

/// <summary>
/// Real-time channel for a Kanban board (Phase 2 §3). Clients JoinBoard/LeaveBoard to
/// subscribe to events broadcast to the group "board-{boardId}" (tech decision §2.2).
/// Group membership is scoped to workspace members so board events never leak.
/// </summary>
public sealed class BoardHub : Hub
{
    public const string GroupPrefix = "board-";

    // Event names broadcast by services via IHubContext<BoardHub> (Phase 2 §3.2).
    public const string TaskCreated = nameof(TaskCreated);
    public const string TaskUpdated = nameof(TaskUpdated);
    public const string TaskMoved = nameof(TaskMoved);
    public const string TaskDeleted = nameof(TaskDeleted);
    public const string ColumnCreated = nameof(ColumnCreated);
    public const string ColumnUpdated = nameof(ColumnUpdated);
    public const string ColumnsReordered = nameof(ColumnsReordered);
    public const string ColumnDeleted = nameof(ColumnDeleted);
    public const string CommentAdded = nameof(CommentAdded);
    public const string CommentDeleted = nameof(CommentDeleted);

    public static string GroupName(Guid boardId) => $"{GroupPrefix}{boardId}";

    private readonly IServiceScopeFactory _scopeFactory;

    public BoardHub(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task JoinBoard(string boardId)
    {
        var board = ParseBoardId(boardId);
        await EnsureMemberAsync(board, Context.User?.FindFirstValue(ClaimTypes.NameIdentifier));
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(board));
    }

    public async Task LeaveBoard(string boardId)
    {
        var board = ParseBoardId(boardId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(board));
    }

    private static Guid ParseBoardId(string boardId)
    {
        if (!Guid.TryParse(boardId, out var id))
        {
            throw new HubException("Invalid board id.");
        }

        return id;
    }

    /// <summary>
    /// A user may only join the group of a board that lives in a workspace they belong to.
    /// Runs in a short-lived scope so scoped services never leak across connection lifetimes.
    /// </summary>
    private async Task EnsureMemberAsync(Guid boardId, string? userIdClaim)
    {
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            throw new HubException("Not allowed to join this board.");
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TeamNexusDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<IWorkspaceAccess>();

        var board = await db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == boardId);

        if (board is null)
        {
            throw new HubException("Board not found.");
        }

        try
        {
            await access.RequireMemberAsync(board.WorkspaceId, userId);
        }
        catch (BoardModuleException)
        {
            // Do not reveal whether the board/workspace exists.
            throw new HubException("Not allowed to join this board.");
        }
    }
}

/// <summary>Maps the BoardHub endpoint (called from Program.cs after authorization).</summary>
public static class BoardHubEndpoints
{
    public static IEndpointRouteBuilder MapBoardHub(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<BoardHub>("/hubs/board")
            .RequireAuthorization();

        return endpoints;
    }
}
