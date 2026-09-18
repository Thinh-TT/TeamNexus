namespace TeamNexus.Modules.Ai.DTOs;

/// <summary>
/// One message the client sends back for the AI Task Chat (Phase 14 §2.3).
/// <para>
/// The server keeps <b>no</b> chat table (decision D2), so the transcript lives in the browser and is
/// replayed on every turn. That makes this list the entire conversation state — which is also why it is
/// validated (count, total length, role vocabulary) <b>before</b> any provider call.
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
