namespace TeamNexus.Modules.Board.DTOs;

/// <summary>SignalR payload for a task move (event name "TaskMoved").</summary>
public sealed record TaskMovedEventPayload(Guid TaskId, Guid FromColumnId, Guid ToColumnId, int Position);
