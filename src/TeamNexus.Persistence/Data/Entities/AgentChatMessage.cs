namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Who wrote one chat turn. Stored as text + CHECK (DB design §4), mirroring
/// <c>AiActionLog.Status</c>/<c>AgentRun.Status</c>.
/// <para>
/// <c>system</c> is deliberately <b>absent</b>: the system prompt is composed on the server at request
/// time and is never a row, so accepting it from a client would only create a way to smuggle
/// instructions into stored history.
/// </para>
/// </summary>
public enum AgentChatRole
{
    User,
    Assistant,
}

/// <summary>
/// One persisted turn of a board-scoped AI Agent Chat (Phase 14.5 §B).
/// <para>
/// <b>Why this table exists at all.</b> Phase 14 kept the task-chat transcript in the browser on
/// purpose (D2) and that decision is still in force for the task tab. The board chat is a different
/// thing: it is the long-lived entry point of a board, it can carry several attached tasks, and it
/// produces proposals that a Manager approves later — so the conversation has to survive a reload and
/// be the same for whoever opens the board.
/// </para>
/// <para>
/// <b>Append-only, no query filter, no soft delete.</b> Faithful to <c>ai_action_logs</c> and
/// <c>agent_runs</c>: a chat turn is an audit record, and hiding history would make the proposal trail
/// impossible to reconstruct. Retention is handled by <c>AgentChatRetentionRunner</c>, not by
/// <c>deleted_at</c>.
/// </para>
/// </summary>
public class AgentChatMessage : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Board this conversation belongs to. Null for a task-scoped turn — the CHECK constraint
    /// <c>ck_agent_chat_messages_scope</c> requires exactly one of <see cref="BoardId"/>/<see cref="TaskId"/>.
    /// <para>
    /// <see cref="TaskId"/> is the <b>reserved</b> half of that pair: it is what lets a future phase move
    /// the task tab onto server storage <b>without another migration</b>. The AI Agent Chat only ever
    /// writes <see cref="BoardId"/>.
    /// </para>
    /// </summary>
    public Guid? BoardId { get; set; }

    /// <summary>Task a task-scoped turn belongs to; null for every board-chat turn.</summary>
    public Guid? TaskId { get; set; }

    /// <summary>Denormalised workspace id: retention and pruning are per workspace, not per board.</summary>
    public Guid WorkspaceId { get; set; }

    /// <summary>Author of a <see cref="AgentChatRole.User"/> turn; null for an assistant turn.</summary>
    public Guid? UserId { get; set; }

    public AgentChatRole Role { get; set; }

    /// <summary>
    /// Turn text. A prompt-answer pair is stored as two rows, so the transcript can be replayed to the
    /// model in order. Capped by <c>AgentChat:MaxMessageChars</c> at the service layer.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// jsonb — the tasks the user had attached when they asked: <c>[{ "taskId", "title" }]</c>.
    /// Stored for the transcript UI and for auditing <i>what the answer was allowed to see</i>; it is
    /// never re-read as context (the live board state is).
    /// </summary>
    public string? Attachments { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    // Deliberately NO navigation properties. `Workspace` (and `Board`/`BoardTask`) carry a global
    // query filter, and a required navigation from a filtered-out principal makes EF warn that a
    // filtered join can silently drop rows. Declaring the relationships FK-only — exactly what
    // AiObserverRun does — keeps the model honest: the transcript is read by id, never by walking a
    // navigation, and the service loads the names it needs explicitly.
}
