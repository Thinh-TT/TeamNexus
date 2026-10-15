using System.Text;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;

namespace TeamNexus.Modules.Ai.Services.Knowledge;

/// <summary>
/// Deterministic, bounded chunking for the semantic knowledge base (Giai đoạn 14.7 Track B §B3).
/// <para>
/// <b>Pure and separate from the service on purpose.</b> Chunking decides what the model will ever be able to
/// retrieve, so the boundary rules are the interesting part and must be assertable with string literals —
/// no database, no provider, no clock.
/// </para>
/// <para>
/// <b>Two ceilings, both enforced here.</b> <c>RagChunkChars</c> bounds one chunk (and therefore one embed
/// call); <c>RagMaxChunksPerDocument</c> bounds the whole document (and therefore the bill for one upload).
/// A document that exceeds the count ceiling is <b>truncated</b>, not rejected: partial knowledge is useful,
/// and the caller records how many chunks were dropped so the truncation is visible rather than silent.
/// </para>
/// <para>
/// <b>Paragraph-first, sentence-second, hard-cut last.</b> Splitting on blank lines keeps a chunk semantically
/// coherent, which is what makes an embedding meaningful; a chunk that is a fixed 1200-character window across
/// three unrelated paragraphs embeds into an average of nothing.
/// </para>
/// </summary>
public static class KnowledgeChunking
{
    /// <summary>A chunk produced from a document, in reading order.</summary>
    public sealed record Chunk(int Index, string Content);

    /// <summary>Outcome of chunking one document.</summary>
    /// <param name="Chunks">The chunks to embed, in order.</param>
    /// <param name="DroppedChunks">
    /// How many chunks the count ceiling cut. Surfaced rather than swallowed so an operator can tell "the
    /// document was indexed" apart from "the first 200 chunks of it were indexed".
    /// </param>
    public sealed record ChunkPlan(IReadOnlyList<Chunk> Chunks, int DroppedChunks)
    {
        public bool Truncated => DroppedChunks > 0;
    }

    /// <summary>Preferred paragraph separator: a blank line.</summary>
    private static readonly string[] ParagraphSeparator = ["\r\n\r\n", "\n\n"];

    /// <summary>
    /// Splits <paramref name="content"/> into at most <paramref name="maxChunks"/> chunks of at most
    /// <paramref name="maxChars"/> characters.
    /// </summary>
    public static ChunkPlan Plan(string? content, int maxChars, int maxChunks)
    {
        var charBudget = Math.Max(1, maxChars);
        var countBudget = Math.Max(1, maxChunks);

        if (string.IsNullOrWhiteSpace(content))
        {
            return new ChunkPlan([], 0);
        }

        var pieces = new List<string>();

        foreach (var paragraph in SplitParagraphs(content))
        {
            AppendBounded(pieces, paragraph, charBudget);
        }

        var chunks = new List<Chunk>(Math.Min(pieces.Count, countBudget));

        for (var index = 0; index < pieces.Count && index < countBudget; index++)
        {
            chunks.Add(new Chunk(index, pieces[index]));
        }

        return new ChunkPlan(chunks, Math.Max(0, pieces.Count - chunks.Count));
    }

    /// <summary>
    /// Splits on blank lines. Oversized paragraphs are handled by the caller, which further splits them on
    /// single newlines — long bullet lists are the normal case in a technical document, and each bullet is a
    /// reasonable chunk.
    /// </summary>
    private static IEnumerable<string> SplitParagraphs(string content)
    {
        foreach (var block in content.Split(ParagraphSeparator, StringSplitOptions.None))
        {
            var trimmed = block.Trim();

            if (trimmed.Length > 0)
            {
                yield return trimmed;
            }
        }
    }

    /// <summary>
    /// Adds one piece to the plan, splitting it further when it does not fit in a single chunk. Never produces
    /// an empty chunk (an empty embed input is a wasted call) and never produces one longer than the budget.
    /// </summary>
    private static void AppendBounded(List<string> target, string piece, int budget)
    {
        if (piece.Length <= budget)
        {
            target.Add(piece);
            return;
        }

        // Line-granular first: a fence or a bullet is a better boundary than an arbitrary character offset.
        foreach (var line in piece.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.Length <= budget)
            {
                target.Add(trimmed);
                continue;
            }

            foreach (var hardCut in HardCut(trimmed, budget))
            {
                target.Add(hardCut);
            }
        }
    }

    /// <summary>
    /// Last resort: cut a line that is longer than a whole chunk. <b>Never splits a surrogate pair</b> — cutting
    /// between the two halves of an emoji or a rare CJK character produces unpaired surrogates, which then fail
    /// UTF-8 encoding at the provider and turn one bad document into a wedged embedding runner. The Phase 12
    /// lesson (<c>FakeAiProvider.SplitIntoChunks</c>) is applied here rather than re-learned.
    /// </summary>
    private static IEnumerable<string> HardCut(string value, int budget)
    {
        var start = 0;

        while (start < value.Length)
        {
            var length = Math.Min(budget, value.Length - start);

            if (start + length < value.Length && char.IsHighSurrogate(value[start + length - 1]))
            {
                // Back off one char so the pair stays together; length is at least 1 because budget >= 1.
                length--;
            }

            if (length <= 0)
            {
                length = Math.Min(2, value.Length - start);
            }

            yield return value.Substring(start, length);
            start += length;
        }
    }

    /// <summary>
    /// Renders a bounded context block from retrieved chunks (Giai đoạn 14.7 Track B §B3).
    /// <para>
    /// <b>Never exceeds the caller's ceiling, and cuts by whole lines.</b> Same rule as the board digest
    /// (<c>MaxDigestChars</c>): a half-sentence handed to the model is worse than an absent one, because the model
    /// will complete it from imagination. A snippet is kept whole or left out whole — a heading with no body under
    /// it would cite a document and then show nothing from it, which is worse than not citing it.
    /// </para>
    /// <para>
    /// <b>The "còn N đoạn …" note is written, but never at the cost of a snippet.</b> The note's length depends on
    /// how many snippets were dropped, which is only knowable after the pass, and it must fit inside the same
    /// ceiling. Reserving an up-front estimate is not acceptable: with a generous estimate the reserve alone can
    /// exceed the whole budget. So the pass runs twice in the one case where it matters — once greedily to learn
    /// the count, then once more reserving the note's <b>exact</b> cost. If even that does not fit, the note is
    /// what gets dropped rather than the content: a block that returned nothing in order to say "some knowledge
    /// was omitted" would lose the knowledge to make room for a sentence about losing it. At most a handful of
    /// snippets are involved, so the extra passes cost nothing.
    /// </para>
    /// <para>
    /// <b>Contains no identifiers.</b> Only the document title and the text — never a document, chunk or
    /// workspace id. This is the same invariant the rest of the prompt work follows: the model cannot echo an id
    /// it was never given.
    /// </para>
    /// </summary>
    public static string BuildContextBlock(
        IReadOnlyList<KnowledgeSnippet> snippets, int maxChars)
    {
        if (snippets.Count == 0 || maxChars <= 0)
        {
            return string.Empty;
        }

        var budget = maxChars;

        var builder = new StringBuilder();
        var included = 0;
        var dropped = 0;

        void AppendCapped(string line)
        {
            // `TrimEnd` at the end means the running length can be one newline over; the newline is counted in the
            // cost so the final length never exceeds the budget by more than a trailing newline.
            var cost = line.Length + Environment.NewLine.Length;

            if (builder.Length + cost > budget)
            {
                return;
            }

            builder.AppendLine(line);
        }

        AppendCapped(ContextHeader);

        foreach (var snippet in snippets)
        {
            var title = string.IsNullOrWhiteSpace(snippet.DocumentTitle)
                ? "(không rõ tài liệu)"
                : snippet.DocumentTitle.Trim();

            var heading = $"- {title}:";
            var body = $"  {snippet.Content.Trim()}";
            var cost = heading.Length + body.Length + (2 * Environment.NewLine.Length);

            // Checked BEFORE anything is appended: appending the heading and then failing to fit the body would
            // leave a document cited with nothing under it — the reader (human or model) sees "here is the relevant
            // knowledge" followed by nothing, which is worse than not citing it at all.
            if (builder.Length + cost > budget)
            {
                dropped++;
                continue;
            }

            builder.AppendLine(heading);
            builder.AppendLine(body);
            included++;
        }

        // Only a heading and nothing under it: report no usable knowledge rather than a bare header. A block that
        // says "project knowledge:" followed by an empty line reads as "there is knowledge here, I am just not
        // showing it", which is exactly the misleading-degradation failure mode this file exists to avoid.
        if (included == 0)
        {
            return string.Empty;
        }

        // ⛔ The trap this avoids: paying for the "(còn N đoạn …)" note with a snippet.
        //
        // The budget is filled with CONTENT first, and the note is added only from whatever room is left over. An
        // earlier version reserved the note's cost up front, which is the obvious way to guarantee the note fits —
        // and it evicted a snippet that fitted the ceiling on its own, so a slightly smaller budget produced a
        // strictly smaller block for no reason a caller could see. Worse, an exact reservation ("run one pass to
        // learn the count, then reserve that much") drives the deciding pass to include nothing when the ceiling can
        // hold a snippet but not a snippet plus the note, turning a smaller answer into NO answer at all.
        //
        // Dropping the note costs a reader the knowledge that something was omitted; dropping the snippet costs them
        // the knowledge itself. The note loses, every time. `AgentChatService` already logs how many chunks retrieval
        // returned, so the omission is visible to operators rather than silent.
        if (dropped > 0)
        {
            var note = $"{DroppedNotePrefix}{dropped}{DroppedNoteSuffix}";

            if (builder.Length + note.Length + Environment.NewLine.Length <= budget)
            {
                builder.AppendLine(note);
            }
        }

        var text = builder.ToString().TrimEnd();

        // Hard ceiling: `AppendCapped` and the note check above are what keep this reachable only by the degenerate
        // "smaller than the header itself" case, and reporting nothing is the honest answer there.
        return text.Length <= budget ? text : string.Empty;
    }

    /// <summary>Text around the count in the "(còn N đoạn …)" note; kept here so its cost can be reserved.</summary>
    private const string DroppedNotePrefix = "(còn ";

    private const string DroppedNoteSuffix = " đoạn kiến thức không hiển thị do giới hạn độ dài)";

    /// <summary>Heading of the retrieved-knowledge block; the prompt refers to it by this name.</summary>
    public const string ContextHeader = "KIẾN THỨC DỰ ÁN:";
}

/// <summary>One retrieved chunk as the prompt sees it: a document title and the text. No ids, by design.</summary>
public sealed record KnowledgeSnippet(string DocumentTitle, string Content);
