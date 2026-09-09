using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>Column CRUD + reorder (Phase 2 §2.3) under /api/boards/{boardId}/columns.</summary>
public static class ColumnsEndpoints
{
    public static IEndpointRouteBuilder MapColumnsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/boards/{boardId:guid}/columns")
            .WithTags("Columns")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapGet("/", GetColumnsAsync).RequireAuthorization();
        group.MapPost("/", CreateColumnAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();
        group.MapPut("/reorder", ReorderColumnsAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();
        group.MapPut("/{columnId:guid}", UpdateColumnAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();
        group.MapDelete("/{columnId:guid}", DeleteColumnAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    private static async Task<IResult> GetColumnsAsync(
        Guid boardId,
        HttpContext http,
        IColumnService columns,
        CancellationToken ct)
    {
        var result = await columns.GetColumnsAsync(boardId, http.RequireUserId(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateColumnAsync(
        Guid boardId,
        CreateColumnRequest request,
        HttpContext http,
        IColumnService columns,
        CancellationToken ct)
    {
        var column = await columns.CreateColumnAsync(boardId, request, http.RequireUserId(), ct);
        return Results.Created($"/api/boards/{boardId}/columns/{column.Id}", column);
    }

    private static async Task<IResult> UpdateColumnAsync(
        Guid boardId,
        Guid columnId,
        UpdateColumnRequest request,
        HttpContext http,
        IColumnService columns,
        CancellationToken ct)
    {
        var column = await columns.UpdateColumnAsync(columnId, request, http.RequireUserId(), ct);
        return column.BoardId == boardId
            ? Results.Ok(column)
            : Results.NotFound(new { error = "Column not found." });
    }

    private static async Task<IResult> ReorderColumnsAsync(
        Guid boardId,
        ReorderColumnsRequest request,
        HttpContext http,
        IColumnService columns,
        CancellationToken ct)
    {
        await columns.ReorderColumnsAsync(boardId, request, http.RequireUserId(), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteColumnAsync(
        Guid boardId,
        Guid columnId,
        HttpContext http,
        IColumnService columns,
        CancellationToken ct)
    {
        // Load first to confirm the column belongs to this board before deleting.
        var existing = await columns.GetColumnsAsync(boardId, http.RequireUserId(), ct);
        if (existing.All(c => c.Id != columnId))
        {
            return Results.NotFound(new { error = "Column not found." });
        }

        await columns.DeleteColumnAsync(columnId, http.RequireUserId(), ct);
        return Results.NoContent();
    }
}
