using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Executes one <see cref="AiActionTypes"/> against real data. <see cref="AiActionService"/> owns
/// the log lifecycle (guard, CAS transition, transaction) and delegates the actual writes here, so
/// adding a new AI write action means adding one applier + one DI line — never editing the service.
/// </summary>
public interface IAiActionApplier
{
    /// <summary>Matches <c>ai_action_logs.action</c> (case-insensitive).</summary>
    string ActionType { get; }

    /// <summary>Applies the action. Runs inside the caller's transaction: throwing rolls everything back.</summary>
    Task<AiActionAppliedResult> ApplyAsync(AiActionLog log, AiActionContext ctx, CancellationToken ct = default);

    /// <summary>
    /// Reverts a previously applied action. Returns warnings to merge into
    /// <c>ai_action_logs.applied_snapshot</c> (e.g. a label that had to be kept).
    /// </summary>
    Task<IReadOnlyList<string>> UndoAsync(AiActionLog log, AiActionContext ctx, CancellationToken ct = default);
}

/// <summary>
/// Resolved scope of one AI action, produced by the service (board + workspace + acting user).
/// <para>
/// <paramref name="TaskId"/> is set for task-scoped actions (Phase 7 <c>entity_type = 'Task'</c>) and
/// stays <c>null</c> for board-scoped ones, so <c>CreateSubtasksApplier</c> needed no change.
/// </para>
/// </summary>
public sealed record AiActionContext(
    Guid BoardId,
    Guid WorkspaceId,
    Guid ActingUserId,
    Guid? TaskId = null);

/// <summary>
/// What an applier actually wrote — persisted into <c>applied_snapshot</c> and used by Undo.
/// <para>
/// <see cref="CreatedCommentId"/>/<see cref="CreatedAttachmentId"/> were added in Phase 7 so a
/// task-scoped output can be undone; both are nullable, which keeps every Phase 4 caller and the
/// existing jsonb shape readable (the extra keys are additive).
/// </para>
/// </summary>
public sealed record AiActionAppliedResult(
    string EntityType,
    Guid? EntityId,
    IReadOnlyList<Guid> CreatedTaskIds,
    IReadOnlyList<Guid> CreatedLabelIds,
    IReadOnlyList<string> Warnings,
    Guid? CreatedCommentId = null,
    Guid? CreatedAttachmentId = null);
