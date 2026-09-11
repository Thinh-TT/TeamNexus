namespace TeamNexus.Modules.Board.DTOs;

/// <summary>SignalR payload for a task move (event name "TaskMoved").</summary>
public sealed record TaskMovedEventPayload(Guid TaskId, Guid FromColumnId, Guid ToColumnId, int Position);

/// <summary>
/// SignalR payload for AI Agent progress (event name "AgentRunProgress", Phase 7 D8).
/// <para>
/// Broadcast by the Ai module's orchestrator through <c>IBoardEventPublisher</c> when a run starts
/// and when it reaches a terminal state — it carries <b>state changes only</b>, never streamed
/// tokens. Shape mirrors the frontend contract (Phase 7 §5.1c) exactly.
/// </para>
/// </summary>
public sealed record AgentRunProgressEventPayload(
    Guid RunId,
    Guid TaskId,
    Guid BoardId,
    string Status,
    string? StopReason,
    int ToolCallCount,
    int TotalTokens,
    string? ClarificationQuestion);
