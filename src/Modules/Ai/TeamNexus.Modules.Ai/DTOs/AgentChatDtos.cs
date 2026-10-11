namespace TeamNexus.Modules.Ai.DTOs;

/// <summary>
/// One task the user attached to a board-chat question (Phase 14.5 §B).
/// <para>
/// Only the id crosses the wire. The <b>context</b> is read on the server from the live board, so a
/// client cannot influence what the model sees beyond choosing which tasks are in scope — and a task
/// that has since changed is described as it is now, not as the browser remembered it.
/// </para>
/// </summary>
public sealed record AgentChatAttachmentRequest(Guid TaskId);

/// <summary>
/// Body of <c>POST /api/boards/{boardId}/agent-chat/stream</c>.
/// </summary>
/// <param name="Messages">
/// The conversation so far. Unlike the task chat (Phase 14 D2), the server <b>also</b> stores these
/// turns; the client still replays them because that keeps one code path for both chat surfaces and lets
/// the request be judged on its own.
/// </param>
/// <param name="AttachedTaskIds">
/// Tasks the user dragged into the panel. Capped by <c>AgentChat:MaxAttachedTasks</c> and rejected (400)
/// if any id is outside this board.
/// </param>
public sealed record AgentChatSendRequest(
    IReadOnlyList<AiChatMessageRequest>? Messages,
    IReadOnlyList<AgentChatAttachmentRequest>? AttachedTaskIds);

/// <summary>
/// Body of <c>POST /api/boards/{boardId}/agent-chat/message</c>: the answer the user decided to keep as a
/// task comment.
/// </summary>
/// <param name="Content">The answer text; 1..<c>AgentChat:MaxAnswerChars</c> (2000).</param>
/// <param name="TaskId">
/// Which attached task the comment belongs to. <c>null</c> means "the first attached task" — the common
/// case of a single attached card. With <b>no</b> attached task there is nowhere to post, so the request
/// is a 400 rather than a silent guess.
/// </param>
public sealed record SaveAgentChatAnswerRequest(string? Content, Guid? TaskId);

/// <summary>A stored board-chat turn as the API returns it.</summary>
public sealed record AgentChatMessageResponse(
    Guid Id,
    string Role,
    string Content,
    IReadOnlyList<AgentChatAttachmentResponse> Attachments,
    DateTimeOffset CreatedAt);

/// <summary>One attachment as persisted alongside a stored turn.</summary>
public sealed record AgentChatAttachmentResponse(Guid TaskId, string Title);
