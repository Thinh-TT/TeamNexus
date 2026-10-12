using TeamNexus.Modules.Ai.Services;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Phase 14.6 §A2 — the identifier redactor behind "AI Chat never shows a raw Guid".
/// <para>
/// <b>Why this needs its own suite.</b> The prompt rule is unverifiable by construction (a model may ignore
/// it), so the only thing that can actually be locked down is the filter — and a filter is exactly the kind
/// of code that passes a happy-path assertion while redacting half of a real sentence or eating a legitimate
/// identifier inside a proposal. Every case below is one of those failure modes.
/// </para>
/// </summary>
public sealed class AiChatAnswerSanitizerTests
{
    /// <summary>The exact leak observed on the deployed build (Phase 14.6 §A2).</summary>
    private const string LeakedSentence =
        "Sau khi có thẻ trong ngữ cảnh, mình sẽ đề xuất chuyển sang cột \"process\" "
        + "(id: 9724c884-d103-4147-9b68-7532d97f04a9).";

    [Fact]
    public void SANITIZE1_TheObservedLeakIsRedactedAndTheSentenceSurvives()
    {
        var result = AiChatAnswerSanitizer.RedactIdentifiers(LeakedSentence);

        Assert.DoesNotContain("9724c884", result, StringComparison.Ordinal);
        Assert.Contains(AiChatAnswerSanitizer.Placeholder, result, StringComparison.Ordinal);

        // The sentence must still read as a sentence: deleting the whole fragment would hide that the model
        // mentioned an id at all, and a reader could not tell a redaction from a hallucination.
        Assert.StartsWith("Sau khi có thẻ trong ngữ cảnh", result, StringComparison.Ordinal);
        Assert.EndsWith(").", result, StringComparison.Ordinal);
        Assert.Contains("cột \"process\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SANITIZE2_RedactionIsIdempotent()
    {
        var once = AiChatAnswerSanitizer.RedactIdentifiers(LeakedSentence);
        var twice = AiChatAnswerSanitizer.RedactIdentifiers(once);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void SANITIZE3_EveryIdentifierInAListIsRedacted()
    {
        var columnId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var text = $"Cột {columnId} chứa thẻ {taskId} — cả hai đều không nên hiện ra.";

        var result = AiChatAnswerSanitizer.RedactIdentifiers(text);

        Assert.False(AiChatAnswerSanitizer.ContainsIdentifier(result));
        Assert.Equal(
            $"Cột {AiChatAnswerSanitizer.Placeholder} chứa thẻ {AiChatAnswerSanitizer.Placeholder} — "
            + "cả hai đều không nên hiện ra.",
            result);
    }

    [Fact]
    public void SANITIZE4_AnUppercaseIdentifierIsCaughtToo()
    {
        var id = Guid.NewGuid();
        var text = $"Thẻ (id: {id.ToString().ToUpperInvariant()}) nằm ở cột Đang làm.";

        var result = AiChatAnswerSanitizer.RedactIdentifiers(text);

        Assert.False(AiChatAnswerSanitizer.ContainsIdentifier(result));
        Assert.DoesNotContain(id.ToString("D"), result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nằm ở cột Đang làm.", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SANITIZE5_ALongerHexBlobIsNotMistakenForAnIdentifier()
    {
        // 36 characters of the right shape can sit inside a longer hyphenated hex run (a digest, an ETag).
        // A bare `\b` would have matched a slice of it and mangled a legitimate value, which is why the
        // pattern is bounded by lookarounds instead.
        var digest = "deadbeef-dead-beef-dead-beefdeadbeefdead";
        var sha = new string('a', 64);

        Assert.Equal(digest, AiChatAnswerSanitizer.RedactIdentifiers(digest));
        Assert.Equal(sha, AiChatAnswerSanitizer.RedactIdentifiers(sha));
        Assert.False(AiChatAnswerSanitizer.ContainsIdentifier(digest));
    }

    [Fact]
    public void SANITIZE6_PlainProseAndEmptyInputAreUntouched()
    {
        const string prose = "Cột \"Đang làm\" có 3 thẻ quá hạn, nên xử lý trước.";

        Assert.Equal(prose, AiChatAnswerSanitizer.RedactIdentifiers(prose));
        Assert.Equal(string.Empty, AiChatAnswerSanitizer.RedactIdentifiers(null));
        Assert.Equal(string.Empty, AiChatAnswerSanitizer.RedactIdentifiers(string.Empty));
        Assert.False(AiChatAnswerSanitizer.ContainsIdentifier(null));
        Assert.False(AiChatAnswerSanitizer.ContainsIdentifier(string.Empty));
    }

    [Fact]
    public void SANITIZE7_TheFilterIsNotFenceAwareByDesign()
    {
        // This is the documented boundary, asserted so it cannot be "fixed" by accident: the redactor knows
        // nothing about proposal blocks, because it is only ever handed the parser's VISIBLE text. Feeding it
        // a whole answer would redact the real `taskId`/`columnId` the Accountability Layer needs — which is
        // exactly why `AgentChatService` splits first. (The integration suite proves the block survives.)
        var block = """{"proposal":{"kind":"MoveTaskToColumn","payload":{"taskId":"9724c884-d103-4147-9b68-7532d97f04a9"}}}""";

        var result = AiChatAnswerSanitizer.RedactIdentifiers(block);

        Assert.Contains(AiChatAnswerSanitizer.Placeholder, result, StringComparison.Ordinal);
        Assert.DoesNotContain("9724c884", result, StringComparison.Ordinal);
    }
}
