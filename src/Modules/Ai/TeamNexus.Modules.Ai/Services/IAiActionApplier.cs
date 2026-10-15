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
/// <para>
/// <b><paramref name="BoardId"/> became nullable in Phase 14 (§6, P1).</b> The board-template action
/// <i>creates</i> a board, so there is no board to point at when it runs. It stays non-null for every
/// board-scoped action, and <see cref="RequireBoardId"/> is what those appliers call so a scope that
/// really should have had a board fails loudly instead of silently acting on <c>Guid.Empty</c>.
/// </para>
/// </summary>
public sealed record AiActionContext(
    Guid? BoardId,
    Guid WorkspaceId,
    Guid ActingUserId,
    Guid? TaskId = null)
{
    /// <summary>
    /// The board this action is scoped to. Throws for a workspace-scoped action (which by definition has
    /// none) — a bug that must surface immediately rather than address the wrong board.
    /// </summary>
    public Guid RequireBoardId() => BoardId
        ?? throw new InvalidOperationException(
            "This AI action is workspace-scoped and has no board; the applier must not ask for one.");

    /// <summary>The task this action is scoped to, or a throw when the action is not task-scoped.</summary>
    public Guid RequireTaskId() => TaskId
        ?? throw new InvalidOperationException(
            "This AI action is not task-scoped; the applier must not ask for a task id.");
}

/// <summary>
/// What an applier actually wrote — persisted into <c>applied_snapshot</c> and used by Undo.
/// <para>
/// <see cref="CreatedCommentId"/>/<see cref="CreatedAttachmentId"/> were added in Phase 7 so a
/// task-scoped output can be undone; both are nullable, which keeps every Phase 4 caller and the
/// existing jsonb shape readable (the extra keys are additive).
/// </para>
/// <para>
/// <see cref="CreatedColumnIds"/> was added in Phase 14 (§6, P3) for the board template. It is optional
/// and defaults to <c>null</c> so no existing applier has to be touched, and the snapshot writer emits
/// it only when present.
/// </para>
/// <para>
/// <see cref="UpdatedTaskId"/>/<see cref="FromColumnId"/> were added in Phase 14.5 (§B) for the two
/// field-level actions the board chat can propose. Both are optional for the same reason: the snapshot
/// writer omits nulls, so every earlier snapshot keeps its exact previous key set.
/// </para>
/// </summary>
public sealed record AiActionAppliedResult(
    string EntityType,
    Guid? EntityId,
    IReadOnlyList<Guid> CreatedTaskIds,
    IReadOnlyList<Guid> CreatedLabelIds,
    IReadOnlyList<string> Warnings,
    Guid? CreatedCommentId = null,
    Guid? CreatedAttachmentId = null,
    IReadOnlyList<Guid>? CreatedColumnIds = null,
    Guid? CreatedBoardId = null,
    Guid? UpdatedTaskId = null,
    Guid? FromColumnId = null,
    // ---- Giai đoạn 14.7 §A5 Phần B: the recovery-plan undo basis -------------------------------
    // All optional and null-omitted by the snapshot writer, so no pre-14.7 snapshot gains a key.
    /// <summary>Which <c>RecoveryPlanKinds</c> value was applied — Undo switches on it.</summary>
    string? RecoveryKind = null,
    /// <summary>The card the plan acted on (null for a workspace-wide escalation).</summary>
    Guid? TargetTaskId = null,
    /// <summary>Assignment BEFORE the plan, so Undo restores it exactly.</summary>
    Guid? PreviousAssigneeId = null,
    /// <summary>Assignment the plan wrote — Undo refuses to overwrite a later manual change away from it.</summary>
    Guid? NewAssigneeId = null,
    /// <summary>
    /// Whether the task had a due date at all. Needed because "no date" and "date = null" are the same value
    /// in the column, so the flag is what makes Undo able to restore the ABSENCE of a deadline.
    /// </summary>
    bool? PreviousDueDateHadValue = null,
    DateTimeOffset? PreviousDueDate = null,
    // ---- Giai đoạn 14.7 Track B §B3: the knowledge-document undo basis -------------------------
    /// <summary>
    /// The <c>workspace_knowledge_documents</c> row this action created. Optional and null-omitted by the
    /// snapshot writer for the same reason as every field above: no earlier snapshot gains a key.
    /// </summary>
    Guid? CreatedDocumentId = null,
    /// <summary>How many chunks were written, so the history shows the size of what was indexed.</summary>
    int? CreatedChunkCount = null);
