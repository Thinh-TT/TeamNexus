using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>Board CRUD (Phase 2 §2.2) under /api/workspaces/{workspaceId}/boards.</summary>
public static class BoardsEndpoints
{
    public static IEndpointRouteBuilder MapBoardsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/boards")
            .WithTags("Boards")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapGet("/", GetBoardsAsync).RequireAuthorization();
        group.MapGet("/{boardId:guid}", GetBoardAsync).RequireAuthorization();
        group.MapPost("/", CreateBoardAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();
        group.MapPut("/{boardId:guid}", UpdateBoardAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();
        group.MapDelete("/{boardId:guid}", DeleteBoardAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    private static async Task<IResult> GetBoardsAsync(
        Guid workspaceId,
        HttpContext http,
        IBoardService boards,
        CancellationToken ct)
    {
        var result = await boards.GetBoardsAsync(workspaceId, http.RequireUserId(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetBoardAsync(
        Guid workspaceId,
        Guid boardId,
        HttpContext http,
        IBoardService boards,
        CancellationToken ct)
    {
        var board = await boards.GetBoardByIdAsync(boardId, http.RequireUserId(), ct);

        // Guard against a mismatched workspaceId in the URL (no resource leak).
        return board.WorkspaceId == workspaceId
            ? Results.Ok(board)
            : Results.NotFound(new { error = "Board not found." });
    }

    private static async Task<IResult> CreateBoardAsync(
        Guid workspaceId,
        CreateBoardRequest request,
        HttpContext http,
        IBoardService boards,
        CancellationToken ct)
    {
        var board = await boards.CreateBoardAsync(workspaceId, request, http.RequireUserId(), ct);
        return Results.Created($"/api/workspaces/{workspaceId}/boards/{board.Id}", board);
    }

    private static async Task<IResult> UpdateBoardAsync(
        Guid workspaceId,
        Guid boardId,
        UpdateBoardRequest request,
        HttpContext http,
        IBoardService boards,
        CancellationToken ct)
    {
        var board = await boards.UpdateBoardAsync(boardId, request, http.RequireUserId(), ct);
        return board.WorkspaceId == workspaceId
            ? Results.Ok(board)
            : Results.NotFound(new { error = "Board not found." });
    }

    private static async Task<IResult> DeleteBoardAsync(
        Guid workspaceId,
        Guid boardId,
        HttpContext http,
        IBoardService boards,
        CancellationToken ct)
    {
        var userId = http.RequireUserId();

        // Confirm the board lives under this workspace before soft-deleting.
        var board = await boards.GetBoardByIdAsync(boardId, userId, ct);
        if (board.WorkspaceId != workspaceId)
        {
            return Results.NotFound(new { error = "Board not found." });
        }

        await boards.DeleteBoardAsync(boardId, userId, ct);
        return Results.NoContent();
    }
}
