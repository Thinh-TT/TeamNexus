using System.Text.RegularExpressions;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Removes database identifiers from the <b>prose</b> an AI answer contains (Phase 14.6 §A2).
/// <para>
/// <b>Why this exists at all.</b> Live testing on the deployed build caught <c>deepseek-chat</c> writing a
/// raw column Guid into a user-facing sentence: <i>"…chuyển sang cột "process"
/// (id: 9724c884-d103-4147-9b68-7532d97f04a9)"</i>. The prompt already asks the model for display names, but
/// a prompt is a <b>request</b>, not an enforcement point — the model is free to ignore it, and the answer
/// is the one artifact that reaches a human. This type is the enforcement point.
/// </para>
/// <para>
/// <b>Scope: prose only.</b> The proposal block legitimately carries real ids (<c>taskId</c>,
/// <c>columnId</c>) — the Accountability Layer needs them to apply anything at all — so callers pass the
/// <i>already extracted</i> visible text (see <c>AgentChatProposalParser.Parse</c>), never the raw answer.
/// Running this over a whole answer would quietly turn a submittable proposal into an invalid one.
/// </para>
/// <para>
/// <b>Pure and idempotent.</b> No database, no clock, no provider, and the placeholder cannot match the
/// pattern again — so sanitizing twice is the same as sanitizing once.
/// </para>
/// </summary>
public static class AiChatAnswerSanitizer
{
    /// <summary>
    /// What an identifier becomes. Deliberately Vietnamese and deliberately visible: silently deleting the
    /// text would make the model's sentence read as if it never mentioned an id, and a reader could not tell
    /// a redaction from a hallucination.
    /// </summary>
    public const string Placeholder = "[mã nội bộ]";

    /// <summary>
    /// Canonical UUID / GUID shape: 8-4-4-4-12 hexadecimal digits. The lookarounds keep the match from
    /// biting a slice out of a longer hex run (a sha256 digest, an ETag), which a bare <c>\b</c> would not
    /// fully prevent: <c>-</c> is not a word character, so a hyphenated hex blob can contain a
    /// boundary-delimited 36-character window that is not an identifier at all.
    /// </summary>
    private static readonly Regex IdentifierPattern = new(
        @"(?<![0-9a-fA-F-])[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(?![0-9a-fA-F-])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Replaces every identifier-shaped token in <paramref name="text"/> with
    /// <see cref="Placeholder"/>. Null/blank input comes back as <see cref="string.Empty"/> — never null —
    /// because every caller stores or renders the result.
    /// </summary>
    public static string RedactIdentifiers(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return IdentifierPattern.Replace(text, Placeholder);
    }

    /// <summary>True when the text still contains something shaped like a database identifier.</summary>
    /// <remarks>
    /// Exposed so an integration test can assert the property directly ("no Guid survives into the stored
    /// transcript") instead of re-implementing the pattern and drifting from it.
    /// </remarks>
    public static bool ContainsIdentifier(string? text)
        => !string.IsNullOrEmpty(text) && IdentifierPattern.IsMatch(text);
}
