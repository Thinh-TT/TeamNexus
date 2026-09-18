using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Renders the Server-Sent Events frames of the AI Task Chat (Phase 14 §2.2, decision D5).
/// <para>
/// <b>Why this is a separate pure type:</b> the part of SSE that is easy to get wrong is the frame
/// format itself — the blank line separator, one <c>data:</c> line per frame, JSON escaping of the
/// payload. Keeping it in a pure function means every one of those details is asserted in a unit test
/// with no database, no HTTP and no stream, instead of being inferred from an integration test that
/// happens to pass.
/// </para>
/// <para>
/// The wire contract (mirrored by <c>phase-14-remaining-frontend-handover.md</c>):
/// <c>meta</c> → <c>delta</c>* → <c>done</c>, or <c>error</c> at any point after the stream opened.
/// </para>
/// </summary>
public static class AiChatSseWriter
{
    /// <summary>Event name carrying the task identity the answer is about.</summary>
    public const string MetaEvent = "meta";

    /// <summary>Event name carrying one piece of the answer.</summary>
    public const string DeltaEvent = "delta";

    /// <summary>Event name carrying the full answer plus token counts; exactly one per stream.</summary>
    public const string DoneEvent = "done";

    /// <summary>
    /// Event name for a failure that happened <b>after</b> the response headers were sent. Failures
    /// before that point are ordinary JSON error responses with a real status code — see
    /// <c>AiChatService</c>.
    /// </summary>
    public const string ErrorEvent = "error";

    /// <summary>Content type the endpoint must send, and the only one <c>fetch</c> + our parser accept.</summary>
    public const string ContentType = "text/event-stream";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Opening frame: where the answer belongs, so a stale tab can notice it is off-task.</summary>
    public static string Meta(AiChatMeta meta) => Frame(MetaEvent, JsonSerializer.Serialize(meta, Json));

    /// <summary>One piece of the answer. An empty delta is skipped (a frame with no text is noise).</summary>
    public static string Delta(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return Frame(DeltaEvent, JsonSerializer.Serialize(new DeltaPayload(text), Json));
    }

    /// <summary>Terminal frame with the complete answer and the token accounting for the run.</summary>
    public static string Done(string answer, int? promptTokens, int? completionTokens)
        => Frame(
            DoneEvent,
            JsonSerializer.Serialize(new DonePayload(answer, promptTokens, completionTokens), Json));

    /// <summary>
    /// Failure frame. The message is sent as plain text inside JSON, never as HTML: the client renders
    /// it through <c>message.error</c>/<c>Alert</c>, and the SSE body is never treated as markup.
    /// </summary>
    public static string Error(string message)
        => Frame(ErrorEvent, JsonSerializer.Serialize(new ErrorPayload(message), Json));

    /// <summary>
    /// One SSE frame: <c>event: …\n</c>, one <c>data: …\n</c>, then the blank line that terminates it.
    /// <para>
    /// The payload is a single JSON object serialized on one line, so it can never contain a raw
    /// newline that would prematurely end the frame — <see cref="JsonSerializer"/> escapes any
    /// <c>\n</c> inside a string value as <c>\\n</c>. That is exactly why JSON is used rather than
    /// hand-built text.
    /// </para>
    /// </summary>
    public static string Frame(string eventName, string jsonPayload)
        => $"event: {eventName}\ndata: {jsonPayload}\n\n";

    /// <summary>Wire shape of the opening frame.</summary>
    public sealed record AiChatMeta(
        Guid TaskId,
        Guid BoardId,
        Guid WorkspaceId,
        string TaskTitle,
        int HistoryMessages,
        string Model);

    private sealed record DeltaPayload(string Text);

    private sealed record DonePayload(
        string Answer,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? PromptTokens,
        int? CompletionTokens);

    private sealed record ErrorPayload(string Error);
}
