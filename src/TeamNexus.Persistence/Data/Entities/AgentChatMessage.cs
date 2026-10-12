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
/// One persisted turn of an AI chat: the board's AI Agent Chat (Phase 14.5 §B) or a task's
/// "Hỏi AI về thẻ này" tab (Phase 14.6 §A3). One table, two scopes, exactly one of which is set.
/// <para>
/// <b>Why this table exists at all.</b> Phase 14 kept the task-chat transcript in the browser on purpose
/// (D2) and that decision is still <i>half</i> in force: the client still sends and replays the history. What
/// changed in 14.6 is that the conversation is now also <b>kept</b> — a board's chat had to survive a reload
/// because it can carry several attached tasks and produces proposals a Manager approves later, and a task's
/// chat had to survive it for the same reason a teammate joining a discussion expects to read what was
/// already said.
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
    /// </summary>
    public Guid? BoardId { get; set; }

    /// <summary>
    /// Task a task-scoped turn belongs to; null for every board-chat turn.
    /// <para>
    /// Reserved as nullable in Migration 11 precisely so the task tab could move onto server storage without
    /// another migration — which is what Phase 14.6 §A3 then did. A task conversation writes
    /// <see cref="TaskId"/> and leaves <see cref="BoardId"/> null, and the CHECK makes that structural, so the
    /// two transcripts can never mix.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// When the turn was written.
    /// <para>
    /// <b>Not sufficient to order a transcript on its own.</b> The audit stamping takes one timestamp per
    /// <c>SaveChanges</c>, so the two rows of an exchange carry the <b>identical</b> value and the tie has to be
    /// broken by <see cref="Role"/> (question before answer). <c>AgentChatTranscriptQuery</c> owns that rule,
    /// and Phase 14.6 §A3 fixed the defect where a random Guid decided it instead.
    /// </para>
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    // Deliberately NO navigation properties. `Workspace` (and `Board`/`BoardTask`) carry a global
    // query filter, and a required navigation from a filtered-out principal makes EF warn that a
    // filtered join can silently drop rows. Declaring the relationships FK-only — exactly what
    // AiObserverRun does — keeps the model honest: the transcript is read by id, never by walking a
    // navigation, and the service loads the names it needs explicitly.
}
