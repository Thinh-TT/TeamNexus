namespace TeamNexus.Modules.Ai.DTOs;

/// <summary>
/// One message the client sends back for the AI Task Chat (Phase 14 §2.3).
/// <para>
/// The transcript lives in the browser and is replayed on every turn, so this list is the entire
/// conversation state for one request — which is why it is validated (count, total length, role vocabulary)
/// <b>before</b> any provider call.
/// </para>
/// <para>
/// <b>Phase 14.6 §A3 changed where it is KEPT, not how it is sent.</b> Phase 14's decision D2 ("the server
/// keeps no chat table") is replaced: each exchange is now also written to <c>agent_chat_messages</c> under
/// <c>task_id</c>, so the tab survives a reload and every teammate can read it. The client still replays the
/// history, so the request shape — and this type — are unchanged.
/// </para>
/// </summary>
/// <param name="Role"><c>"user"</c> or <c>"assistant"</c>. Anything else is a 400.</param>
/// <param name="Content">Message body; must not be blank. <c>system</c> messages are never accepted from a client.</param>
public sealed record AiChatMessageRequest(string Role, string Content);

/// <summary>Body of <c>POST /api/tasks/{taskId}/ai-chat/stream</c>.</summary>
public sealed record AiChatSendRequest(IReadOnlyList<AiChatMessageRequest>? Messages);

/// <summary>
/// Body of <c>POST /api/tasks/{taskId}/ai-chat/message</c>: the answer the user decided to keep.
/// </summary>
public sealed record SaveAiChatMessageRequest(string? Content);
