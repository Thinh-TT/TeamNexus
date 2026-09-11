using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Hubs;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Broadcasts Kanban changes to the SignalR group of a board (Phase 2 §3.2).
/// Services publish AFTER the DB write committed; failure to broadcast is logged and
/// never fails the originating request.
/// </summary>
public interface IBoardEventPublisher
{
    // Task events
    Task TaskCreated(Guid boardId, TaskResponse task, CancellationToken ct = default);
    Task TaskUpdated(Guid boardId, TaskResponse task, CancellationToken ct = default);
    Task TaskMoved(Guid boardId, TaskMovedEventPayload payload, CancellationToken ct = default);
    Task TaskDeleted(Guid boardId, Guid taskId, CancellationToken ct = default);

    // Column events
    Task ColumnCreated(Guid boardId, ColumnResponse column, CancellationToken ct = default);
    Task ColumnUpdated(Guid boardId, ColumnResponse column, CancellationToken ct = default);
    Task ColumnsReordered(Guid boardId, IReadOnlyList<ColumnPositionItem> positions, CancellationToken ct = default);
    Task ColumnDeleted(Guid boardId, Guid columnId, CancellationToken ct = default);

    // Comment events
    Task CommentAdded(Guid boardId, CommentResponse comment, CancellationToken ct = default);
    Task CommentDeleted(Guid boardId, Guid commentId, Guid taskId, CancellationToken ct = default);

    /// <summary>
    /// AI Agent run progress (Phase 7 D8). Published by the Ai module's orchestrator when a run
    /// starts and when it reaches a terminal state — state changes only, never streamed tokens.
    /// </summary>
    Task AgentRunProgress(Guid boardId, AgentRunProgressEventPayload payload, CancellationToken ct = default);
}

public sealed class BoardEventPublisher : IBoardEventPublisher
{
    private readonly IHubContext<BoardHub> _hub;
    private readonly ILogger<BoardEventPublisher> _logger;

    public BoardEventPublisher(IHubContext<BoardHub> hub, ILogger<BoardEventPublisher> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public Task TaskCreated(Guid boardId, TaskResponse task, CancellationToken ct = default)
        => Publish(boardId, BoardHub.TaskCreated, task, ct);

    public Task TaskUpdated(Guid boardId, TaskResponse task, CancellationToken ct = default)
        => Publish(boardId, BoardHub.TaskUpdated, task, ct);

    public Task TaskMoved(Guid boardId, TaskMovedEventPayload payload, CancellationToken ct = default)
        => Publish(boardId, BoardHub.TaskMoved, payload, ct);

    public Task TaskDeleted(Guid boardId, Guid taskId, CancellationToken ct = default)
        => Publish(boardId, BoardHub.TaskDeleted, new { taskId }, ct);

    public Task ColumnCreated(Guid boardId, ColumnResponse column, CancellationToken ct = default)
        => Publish(boardId, BoardHub.ColumnCreated, column, ct);

    public Task ColumnUpdated(Guid boardId, ColumnResponse column, CancellationToken ct = default)
        => Publish(boardId, BoardHub.ColumnUpdated, column, ct);

    public Task ColumnsReordered(Guid boardId, IReadOnlyList<ColumnPositionItem> positions, CancellationToken ct = default)
        => Publish(boardId, BoardHub.ColumnsReordered, positions, ct);

    public Task ColumnDeleted(Guid boardId, Guid columnId, CancellationToken ct = default)
        => Publish(boardId, BoardHub.ColumnDeleted, new { columnId }, ct);

    public Task CommentAdded(Guid boardId, CommentResponse comment, CancellationToken ct = default)
        => Publish(boardId, BoardHub.CommentAdded, comment, ct);

    public Task CommentDeleted(Guid boardId, Guid commentId, Guid taskId, CancellationToken ct = default)
        => Publish(boardId, BoardHub.CommentDeleted, new { commentId, taskId }, ct);

    public Task AgentRunProgress(Guid boardId, AgentRunProgressEventPayload payload, CancellationToken ct = default)
        => Publish(boardId, BoardHub.AgentRunProgress, payload, ct);

    private async Task Publish(Guid boardId, string eventName, object payload, CancellationToken ct)
    {
        try
        {
            await _hub.Clients
                .Group(BoardHub.GroupName(boardId))
                .SendAsync(eventName, payload, ct);
        }
        catch (Exception ex)
        {
            // Real-time delivery must never break the CRUD request that caused it.
            _logger.LogWarning(ex,
                "Failed to broadcast {EventName} to board {BoardId}.", eventName, boardId);
        }
    }
}
