namespace TeamNexus.Modules.Ai.Services;

using System.Text.Json;

/// <summary>
/// Splits a streamed board-chat answer into the text a human reads and the optional proposal block a
/// human may submit (Phase 14.5 §B).
/// <para>
/// <b>Why the answer carries its own JSON instead of a separate SSE frame.</b> A frame would make the
/// proposal part of the transport, so the board chat and the task chat could no longer share one SSE
/// contract, and a lost frame would silently drop a proposal. Keeping it <i>in</i> the answer means the
/// transcript stored in the database contains everything needed to rebuild the card later, and the
/// parser is a pure function over a string.
/// </para>
/// <para>
/// <b>Failures are silent, never thrown.</b> A model that writes malformed JSON must still produce a
/// readable answer, so a bad block yields <c>null</c> for the proposal and the raw text for display. The
/// strict validation happens later, when the user actually asks to submit.
/// </para>
/// </summary>
public static class AgentChatProposalParser
{
    /// <summary>The split result: text always present, proposal block present only when one was found.</summary>
    /// <param name="VisibleText">
    /// The answer with the proposal block removed (and trimmed). Never null; possibly empty when the model
    /// answered with nothing but JSON — the caller shows a generic label rather than a blank bubble.
    /// </param>
    /// <param name="JsonBlock">The inner JSON text of the last fenced <c>json</c> block, or <c>null</c>.</param>
    public sealed record ParseResult(string VisibleText, string? JsonBlock);

    /// <summary>
    /// Finds the <b>last</b> fenced <c>```json</c> block.
    /// <para>
    /// Last, not first, on purpose: the prompt asks for the explanation first and the JSON last, and a
    /// model that shows an <i>example</i> block while explaining will have the real proposal afterwards.
    /// Taking the first block would then submit the example.
    /// </para>
    /// </summary>
    public static ParseResult Parse(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return new ParseResult(string.Empty, null);
        }

        var text = answer;
        var openIndex = text.LastIndexOf("```json", StringComparison.OrdinalIgnoreCase);

        if (openIndex < 0)
        {
            return new ParseResult(text.Trim(), null);
        }

        // The block body starts after the language token and its newline.
        var bodyStart = text.IndexOf('\n', openIndex);
        if (bodyStart < 0)
        {
            return new ParseResult(text.Trim(), null);
        }

        var closeIndex = text.IndexOf("```", bodyStart, StringComparison.Ordinal);
        if (closeIndex < 0)
        {
            // Unterminated fence: the model was cut off. Nothing to submit, and the raw text is still the
            // most honest thing to show.
            return new ParseResult(text.Trim(), null);
        }

        var json = text[(bodyStart + 1)..closeIndex].Trim();

        // Everything else stays visible — text before the block and, unusually, after it.
        var visible = (text[..openIndex] + text[(closeIndex + 3)..]).Trim();

        if (string.IsNullOrWhiteSpace(json))
        {
            return new ParseResult(visible, null);
        }

        return new ParseResult(visible, UnwrapEnvelope(json));
    }

    /// <summary>
    /// Extracts the <c>proposal</c> object from either shape a caller may hold: the documented envelope
    /// (<c>{ "proposal": { … } }</c>) or the bare proposal itself.
    /// <para>
    /// Two callers need this and they hold different things: <see cref="Parse"/> on the whole answer, and
    /// the submit endpoint on the JSON block the client kept. Routing both through one function is what
    /// guarantees the object the UI previews is the object the server validates. JSON that does not parse
    /// is handed back unchanged — the validator owns the error message, and duplicating the parse here
    /// would give two places to keep in step.
    /// </para>
    /// </summary>
    public static string UnwrapEnvelope(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("proposal", out var proposal)
                && proposal.ValueKind == JsonValueKind.Object)
            {
                return proposal.GetRawText();
            }

            return json;
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
