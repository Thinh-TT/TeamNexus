using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>Comments (Phase 2 §2.5) under /api/tasks/{taskId}/comments.</summary>
public static class CommentsEndpoints
{
    public static IEndpointRouteBuilder MapCommentsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/tasks/{taskId:guid}/comments")
            .WithTags("Comments")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapGet("/", GetCommentsAsync).RequireAuthorization();
        group.MapPost("/", CreateCommentAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();
        group.MapPut("/{commentId:guid}", UpdateCommentAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();
        group.MapDelete("/{commentId:guid}", DeleteCommentAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    private static async Task<IResult> GetCommentsAsync(
        Guid taskId,
        HttpContext http,
        ICommentService comments,
        CancellationToken ct)
    {
        var result = await comments.GetCommentsAsync(taskId, http.RequireUserId(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateCommentAsync(
        Guid taskId,
        CreateCommentRequest request,
        HttpContext http,
        ICommentService comments,
        CancellationToken ct)
    {
        var comment = await comments.CreateCommentAsync(taskId, request, http.RequireUserId(), ct);
        return Results.Created($"/api/tasks/{taskId}/comments/{comment.Id}", comment);
    }

    private static async Task<IResult> UpdateCommentAsync(
        Guid taskId,
        Guid commentId,
        UpdateCommentRequest request,
        HttpContext http,
        ICommentService comments,
        CancellationToken ct)
    {
        var comment = await comments.UpdateCommentAsync(commentId, request, http.RequireUserId(), ct);
        return comment.TaskId == taskId
            ? Results.Ok(comment)
            : Results.NotFound(new { error = "Comment not found." });
    }

    private static async Task<IResult> DeleteCommentAsync(
        Guid taskId,
        Guid commentId,
        HttpContext http,
        ICommentService comments,
        CancellationToken ct)
    {
        await comments.DeleteCommentAsync(commentId, http.RequireUserId(), ct);
        return Results.NoContent();
    }
}
