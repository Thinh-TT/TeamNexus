using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>Task CRUD + move (Phase 2 §2.4) under /api/boards/{boardId}/tasks.</summary>
public static class TasksEndpoints
{
    public static IEndpointRouteBuilder MapTasksEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/boards/{boardId:guid}/tasks")
            .WithTags("Tasks")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapGet("/", GetTasksAsync).RequireAuthorization();
        group.MapGet("/{taskId:guid}", GetTaskAsync).RequireAuthorization();
        group.MapPost("/", CreateTaskAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();
        group.MapPut("/{taskId:guid}", UpdateTaskAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();
        group.MapPut("/{taskId:guid}/move", MoveTaskAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();
        group.MapDelete("/{taskId:guid}", DeleteTaskAsync).RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>();

        return endpoints;
    }

    private static async Task<IResult> GetTasksAsync(
        Guid boardId,
        Guid? columnId,
        HttpContext http,
        ITaskService tasks,
        CancellationToken ct)
    {
        var result = await tasks.GetTasksAsync(boardId, columnId, http.RequireUserId(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetTaskAsync(
        Guid boardId,
        Guid taskId,
        HttpContext http,
        ITaskService tasks,
        CancellationToken ct)
    {
        var task = await tasks.GetTaskByIdAsync(taskId, http.RequireUserId(), ct);
        return task.BoardId == boardId
            ? Results.Ok(task)
            : Results.NotFound(new { error = "Task not found." });
    }

    private static async Task<IResult> CreateTaskAsync(
        Guid boardId,
        CreateTaskRequest request,
        HttpContext http,
        ITaskService tasks,
        CancellationToken ct)
    {
        var task = await tasks.CreateTaskAsync(boardId, request, http.RequireUserId(), ct);
        return Results.Created($"/api/boards/{boardId}/tasks/{task.Id}", task);
    }

    private static async Task<IResult> UpdateTaskAsync(
        Guid boardId,
        Guid taskId,
        UpdateTaskRequest request,
        HttpContext http,
        ITaskService tasks,
        CancellationToken ct)
    {
        var task = await tasks.UpdateTaskAsync(taskId, request, http.RequireUserId(), ct);
        return task.BoardId == boardId
            ? Results.Ok(task)
            : Results.NotFound(new { error = "Task not found." });
    }

    private static async Task<IResult> MoveTaskAsync(
        Guid boardId,
        Guid taskId,
        MoveTaskRequest request,
        HttpContext http,
        ITaskService tasks,
        CancellationToken ct)
    {
        var task = await tasks.MoveTaskAsync(taskId, request, http.RequireUserId(), ct);
        return task.BoardId == boardId
            ? Results.Ok(task)
            : Results.NotFound(new { error = "Task not found." });
    }

    private static async Task<IResult> DeleteTaskAsync(
        Guid boardId,
        Guid taskId,
        HttpContext http,
        ITaskService tasks,
        CancellationToken ct)
    {
        // Cheap visibility check for URL/board consistency before the soft delete.
        var task = await tasks.GetTaskByIdAsync(taskId, http.RequireUserId(), ct);
        if (task.BoardId != boardId)
        {
            return Results.NotFound(new { error = "Task not found." });
        }

        await tasks.DeleteTaskAsync(taskId, http.RequireUserId(), ct);
        return Results.NoContent();
    }
}
