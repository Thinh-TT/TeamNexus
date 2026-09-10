namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Activity event names written to <c>activity_logs.action</c> (AI Observer, Phase 5 §2.1).
/// The column is free text (DB design §4): a new event kind is added here and recorded by the
/// service that owns the mutation.
/// </summary>
public static class ObserverActivityActions
{
    public const string TaskCreated = "TaskCreated";

    public const string TaskUpdated = "TaskUpdated";

    public const string TaskMoved = "TaskMoved";

    /// <summary>Emitted in addition to <see cref="TaskMoved"/> when the task enters an is_done column.</summary>
    public const string TaskCompleted = "TaskCompleted";

    public const string TaskDeleted = "TaskDeleted";

    public const string CommentAdded = "CommentAdded";
}

/// <summary>Entity types written to <c>activity_logs.entity_type</c>.</summary>
public static class ObserverEntityTypes
{
    public const string Task = "Task";

    public const string Comment = "Comment";
}

/// <summary>
/// One activity event to record. <see cref="PayloadJson"/> is already-serialized JSON
/// (camelCase) describing a <b>short</b> diff — never a full text dump (token/cost control,
/// DB design §7).
/// </summary>
public sealed record ActivityLogEntry(
    Guid WorkspaceId,
    Guid? BoardId,
    Guid? UserId,
    string EntityType,
    Guid? EntityId,
    string Action,
    string? PayloadJson = null);

/// <summary>
/// Output port for the AI Observer's activity log (Phase 5 §2).
/// <para>
/// The interface deliberately lives in the <b>Board</b> module: Board must not reference the Ai
/// module (Ai already references Board), so Board declares the port and module Ai supplies the
/// EF Core adapter (<c>ActivityLogWriter</c>). Registered by <c>AddBoardModule</c> with a no-op
/// so the Board module stays self-sufficient; Ai overrides that registration.
/// </para>
/// <para>
/// Implementations <b>must never throw</b>: recording activity is a best-effort side effect and
/// a logging failure must not break the CRUD request that triggered it (same rule as
/// <c>IBoardEventPublisher</c>).
/// </para>
/// </summary>
public interface IActivityLogWriter
{
    Task RecordAsync(ActivityLogEntry entry, CancellationToken ct = default);
}

/// <summary>
/// No-op writer used when the Ai module is not registered (module-scoped tests/harnesses) and as
/// the Board module's own default registration (Phase 5 §2.1).
/// </summary>
public sealed class NullActivityLogWriter : IActivityLogWriter
{
    public Task RecordAsync(ActivityLogEntry entry, CancellationToken ct = default)
        => Task.CompletedTask;
}
