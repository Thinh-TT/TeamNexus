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

    /// <summary>
    /// AI Board Template (Phase 14 §4, decision D12): create <b>one</b> board with its columns and its
    /// first tasks, from a proposal the team lead reviewed and confirmed. Scope is
    /// <c>entity_type = 'Workspace'</c>; Apply delegates to <c>IBoardService</c>/<c>IColumnService</c>/
    /// <c>ITaskService</c>, Undo <b>soft-deletes</b> the board (D14).
    /// </summary>
    public const string CreateBoardFromTemplate = "CreateBoardFromTemplate";

    /// <summary>
    /// Board-chat proposal to change an attached task's fields (Phase 14.5 §B). Scope is
    /// <c>entity_type = 'Task'</c>; Apply delegates to <c>ITaskService.UpdateTaskAsync</c> and captures the
    /// task's full previous field set so Undo restores it exactly.
    /// </summary>
    public const string UpdateTaskFields = "UpdateTaskFields";

    /// <summary>
    /// Board-chat proposal to move an attached task to another column of the same board (Phase 14.5 §B).
    /// Scope is <c>entity_type = 'Task'</c>; Apply delegates to <c>ITaskService.MoveTaskAsync</c> and Undo
    /// moves the card back to the column recorded in <c>applied_snapshot.fromColumnId</c>.
    /// </summary>
    public const string MoveTaskToColumn = "MoveTaskToColumn";

    /// <summary>
    /// The AI Observer's proactive recovery plan (Giai đoạn 14.7 §A5 Phần B). Scope is
    /// <c>entity_type = 'Workspace'</c> because a signal can span boards.
    /// <para>
    /// <b>Deliberately ONE action type with the plan kind inside the payload</b>, not four
    /// (<c>ReassignTask</c>/<c>SplitSubtasks</c>/<c>ExtendDeadline</c>/<c>EscalateManager</c>). The column is
    /// free text, so four types would "work" — but the AI history list, the <c>?action=</c> filter and the
    /// single "approve this proposal" UI all read better with one entry whose <c>after_snapshot.kind</c> says
    /// what it will do. Splitting them would fragment the audit trail for no gain.
    /// </para>
    /// </summary>
    public const string RecoveryPlan = "RecoveryPlan";
}

/// <summary>
/// The recovery actions a plan may carry (Giai đoạn 14.7 §A5 Phần B). These strings are the <c>Choice</c>
/// option keys handed to Jev, so they are a contract with the model — and they are also what
/// <c>RecoveryPlanApplier</c> switches on.
/// </summary>
public static class RecoveryPlanKinds
{
    /// <summary>Move the task to a member with capacity.</summary>
    public const string ReassignTask = "ReassignTask";

    /// <summary>Keep the task and break the remaining work into sub-tasks for the next sprint.</summary>
    public const string SplitSubtasks = "SplitSubtasks";

    /// <summary>Extend the deadline to something achievable at the observed velocity.</summary>
    public const string ExtendDeadline = "ExtendDeadline";

    /// <summary>Nothing can be fixed automatically — the manager has to convene.</summary>
    public const string EscalateManager = "EscalateManager";

    /// <summary>All kinds, in the order they are offered to the model.</summary>
    public static IReadOnlyList<string> All { get; } =
        [ReassignTask, SplitSubtasks, ExtendDeadline, EscalateManager];

    /// <summary>Case-insensitive membership test (the model's answer is untrusted input).</summary>
    public static bool IsKnown(string? kind)
        => !string.IsNullOrWhiteSpace(kind)
           && All.Any(known => string.Equals(known, kind.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Canonical casing for a known kind, or <c>null</c>.</summary>
    public static string? Canonical(string? kind)
        => string.IsNullOrWhiteSpace(kind)
            ? null
            : All.FirstOrDefault(known => string.Equals(known, kind.Trim(), StringComparison.OrdinalIgnoreCase));
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

    /// <summary>
    /// Action that <b>creates</b> a board and therefore has no board to be scoped to (Phase 14 §4):
    /// <c>entity_id</c> = workspaceId. Reuses the same
    /// <c>(entity_type, entity_id, created_at)</c> index, so this needed no migration either (D1).
    /// </summary>
    public const string Workspace = "Workspace";
}
