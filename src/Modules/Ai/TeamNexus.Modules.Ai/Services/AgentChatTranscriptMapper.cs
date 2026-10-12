using System.Text.Json;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Maps a stored <see cref="AgentChatMessage"/> row to its API shape (Phase 14.6 §A3).
/// <para>
/// <b>Why this is shared.</b> One table now backs <b>two</b> conversations — a board's chat and a task's
/// chat — and both answer with the same DTO, because the Flutter client (Phase 16) must not have to learn
/// two transcript shapes for the same concept. Keeping the row→DTO mapping in one place is what stops the
/// two surfaces from drifting into "the board chat returns attachments but the task chat forgot to".
/// </para>
/// <para>The queries stay in their own services: a board transcript is keyed by <c>board_id</c> and a task
/// transcript by <c>task_id</c>, and pretending those are one operation would only hide the difference.</para>
/// </summary>
internal static class AgentChatTranscriptMapper
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// One stored turn as the API returns it. A corrupted <c>attachments</c> value is swallowed on purpose:
    /// losing the pills of one historical turn is cosmetic, while throwing would make the whole transcript
    /// unreadable — and the transcript is the only way back into a conversation.
    /// </summary>
    public static AgentChatMessageResponse ToResponse(AgentChatMessage row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var attachments = new List<AgentChatAttachmentResponse>();

        if (!string.IsNullOrWhiteSpace(row.Attachments))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<AgentChatAttachmentResponse>>(row.Attachments, Json);

                if (parsed is not null)
                {
                    attachments.AddRange(parsed.Where(a => a is not null));
                }
            }
            catch (JsonException)
            {
                // Deliberately ignored — see the remarks above.
            }
        }

        return new AgentChatMessageResponse(
            row.Id,
            row.Role == AgentChatRole.User ? "user" : "assistant",
            row.Content,
            attachments,
            row.CreatedAt);
    }
}
