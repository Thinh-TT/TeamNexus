namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Action types stored in <c>ai_action_logs.action</c> (Accountability Layer, Phase 4 §2.1).
/// The column is free text (DB design §4): new AI write actions are added here — each one needs
/// its own <see cref="IAiActionApplier"/> so <see cref="AiActionService"/> stays the single gateway.
/// </summary>
public static class AiActionTypes
{
    /// <summary>Smart Setup confirmation: create a batch of sub-tasks (Phase 3 → Phase 4 handover).</summary>
    public const string CreateSubtasks = "CreateSubtasks";

    /// <summary>
    /// AI Agent output posted as a task comment (Phase 7 §4.7). Scope is <c>entity_type = 'Task'</c>;
    /// Apply delegates to <c>ICommentService</c> with the <b>agent</b> as author, Undo soft-deletes it.
    /// </summary>
    public const string PostComment = "PostComment";

    /// <summary>
    /// AI Agent output stored as a <c>task_attachments</c> row (Phase 7 §4.7). Undo is a
    /// <b>hard delete</b> — the only non-soft-deleted table of the schema (D5).
    /// </summary>
    public const string PostAttachment = "PostAttachment";
}

/// <summary>Entity types stored in <c>ai_action_logs.entity_type</c>.</summary>
public static class AiEntityTypes
{
    /// <summary>Batch action scoped to a board: <c>entity_id</c> = boardId.</summary>
    public const string Board = "Board";

    /// <summary>
    /// Single-entity action scoped to a task: <c>entity_id</c> = taskId (Phase 7 D9). Used by the
    /// agent's <c>PostComment</c>/<c>PostAttachment</c> outputs, which reuse the existing
    /// <c>(entity_type, entity_id, created_at)</c> index — no new index is needed.
    /// </summary>
    public const string Task = "Task";
}
