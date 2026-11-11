using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;

namespace TeamNexus.Api.Tests.Infrastructure;

/// <summary>
/// <see cref="IBoardEventPublisher"/> double for suites that must prove a SignalR broadcast happened
/// without a SignalR client. Registered through <c>ConfigureTestServices</c> (runs after the app's own
/// registrations), exactly like the other recording doubles.
/// <para>
/// Only <see cref="TaskUpdated"/> carries an assertion helper; every other member is a no-op because a
/// test that needs it can read the DB or add a list here.
/// </para>
/// </summary>
public sealed class RecordingBoardEventPublisher : IBoardEventPublisher
{
    private readonly List<(Guid BoardId, TaskResponse Task)> _taskUpdated = [];

    public IReadOnlyList<(Guid BoardId, TaskResponse Task)> TaskUpdatedEvents => _taskUpdated;

    public Task TaskUpdated(Guid boardId, TaskResponse task, CancellationToken ct = default)
    {
        _taskUpdated.Add((boardId, task));
        return Task.CompletedTask;
    }

    public Task TaskCreated(Guid boardId, TaskResponse task, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task TaskMoved(Guid boardId, TaskMovedEventPayload payload, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task TaskDeleted(Guid boardId, Guid taskId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task ColumnCreated(Guid boardId, ColumnResponse column, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task ColumnUpdated(Guid boardId, ColumnResponse column, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task ColumnsReordered(Guid boardId, IReadOnlyList<ColumnPositionItem> positions, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task ColumnDeleted(Guid boardId, Guid columnId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task CommentAdded(Guid boardId, CommentResponse comment, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task CommentDeleted(Guid boardId, Guid commentId, Guid taskId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task AgentRunProgress(Guid boardId, AgentRunProgressEventPayload payload, CancellationToken ct = default)
        => Task.CompletedTask;
}
