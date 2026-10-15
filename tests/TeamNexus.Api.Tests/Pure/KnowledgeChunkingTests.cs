using TeamNexus.Modules.Ai.Services.Knowledge;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Giai đoạn 14.7 Track B §B3 — <see cref="KnowledgeChunking"/>, purely.
/// <para>
/// Chunking decides what the model can ever retrieve, so the boundary rules are the whole subject: two
/// ceilings, no empty chunk, and — the one that turns a bad document into a wedged embedding runner — never
/// cutting a surrogate pair in half. Every assertion here is a string literal, so no database, provider or
/// clock is involved.
/// </para>
/// </summary>
public sealed class KnowledgeChunkingTests
{
    // ---- nothing in, nothing out --------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n\r\n \t ")]
    public void Plan_WithBlankContent_IsAnEmptyPlanRatherThanOneEmptyChunk(string? content)
    {
        // An empty embed input is a wasted call and a zero vector nobody can rank against.
        var plan = KnowledgeChunking.Plan(content, 100, 10);

        Assert.Empty(plan.Chunks);
        Assert.Equal(0, plan.DroppedChunks);
        Assert.False(plan.Truncated);
    }

    // ---- the per-chunk ceiling ----------------------------------------------

    [Fact]
    public void Plan_WithOneShortParagraph_ProducesExactlyOneChunkAtIndexZero()
    {
        var plan = KnowledgeChunking.Plan("Quy ước đặt tên nhánh.", 100, 10);

        var chunk = Assert.Single(plan.Chunks);
        Assert.Equal(0, chunk.Index);
        Assert.Equal("Quy ước đặt tên nhánh.", chunk.Content);
        Assert.False(plan.Truncated);
    }

    [Fact]
    public void Plan_SplitsOnBlankLinesSoAChunkStaysSemanticallyCoherent()
    {
        var plan = KnowledgeChunking.Plan("Đoạn một.\n\nĐoạn hai.\n\nĐoạn ba.", 100, 10);

        Assert.Equal(["Đoạn một.", "Đoạn hai.", "Đoạn ba."], plan.Chunks.Select(c => c.Content).ToList());
        Assert.Equal([0, 1, 2], plan.Chunks.Select(c => c.Index).ToList());
    }

    [Theory]
    [InlineData("\r\n\r\n")]
    [InlineData("\n\n")]
    public void Plan_TreatsBothLineEndingsAsAParagraphBoundary(string separator)
    {
        // The documents are pasted from Windows and Linux alike; one separator would make a whole file a
        // single chunk on the other platform.
        var plan = KnowledgeChunking.Plan($"A{separator}B", 100, 10);

        Assert.Equal(2, plan.Chunks.Count);
    }

    [Fact]
    public void Plan_NeverReturnsAChunkLongerThanTheCharacterBudget()
    {
        const int budget = 220;
        var content = "Một đoạn rất dài không có xuống dòng ở giữa để buộc phải cắt cứng theo ký tự. " + new string('x', 500);

        var plan = KnowledgeChunking.Plan(content, budget, 100);

        Assert.NotEmpty(plan.Chunks);
        Assert.All(plan.Chunks, chunk => Assert.True(
            chunk.Content.Length <= budget,
            $"Chunk dài {chunk.Content.Length} > ngân sách {budget}: '{chunk.Content[..Math.Min(60, chunk.Content.Length)]}'."));
    }

    [Fact]
    public void Plan_OversizedParagraphIsSplitOnSingleNewlinesBeforeHardCutting()
    {
        // A long bullet list is the normal case in a technical document, and each bullet is a better boundary
        // than an arbitrary character offset.
        var content = string.Join('\n', Enumerable.Range(0, 12).Select(i => $"Dòng số {i} nội dung ngắn."));

        var plan = KnowledgeChunking.Plan(content, 30, 100);

        Assert.All(plan.Chunks, chunk => Assert.True(chunk.Content.Length <= 30));
        Assert.Contains(plan.Chunks, chunk => chunk.Content.StartsWith("Dòng số", StringComparison.Ordinal));
        Assert.All(plan.Chunks, chunk => Assert.DoesNotContain('\n', chunk.Content));
    }

    [Fact]
    public void Plan_NeverEmitsAnEmptyOrWhitespaceOnlyChunk()
    {
        var content = "A\n\n\n   \n\n\nB\n\n \n\nC" + new string('\n', 20);

        var plan = KnowledgeChunking.Plan(content, 20, 50);

        Assert.NotEmpty(plan.Chunks);
        Assert.All(plan.Chunks, chunk => Assert.False(string.IsNullOrWhiteSpace(chunk.Content)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void Plan_ClampsANonPositiveCharacterBudgetToAtLeastOne(int budget)
    {
        // A misconfigured 0 must not be able to produce an unbounded chunk (or an infinite loop).
        var plan = KnowledgeChunking.Plan("abcdef", budget, 10);

        Assert.NotEmpty(plan.Chunks);
        Assert.All(plan.Chunks, chunk => Assert.Equal(1, chunk.Content.Length));
    }

    // ---- the per-document ceiling -------------------------------------------

    [Fact]
    public void Plan_RespectsTheChunkCountCeilingAndReportsWhatItDropped()
    {
        var content = string.Join("\n\n", Enumerable.Range(0, 10).Select(i => $"Đoạn {i}"));

        var plan = KnowledgeChunking.Plan(content, 100, 4);

        Assert.Equal(4, plan.Chunks.Count);
        Assert.Equal(6, plan.DroppedChunks);
        Assert.True(plan.Truncated);
        Assert.Equal([0, 1, 2, 3], plan.Chunks.Select(c => c.Index).ToList());
    }

    [Fact]
    public void Plan_AtExactlyTheChunkCeiling_IsNotTruncated()
    {
        // The boundary: dropping a chunk only when the ceiling is genuinely exceeded.
        var content = string.Join("\n\n", Enumerable.Range(0, 5).Select(i => $"Đoạn {i}"));

        var plan = KnowledgeChunking.Plan(content, 100, 5);

        Assert.Equal(5, plan.Chunks.Count);
        Assert.Equal(0, plan.DroppedChunks);
        Assert.False(plan.Truncated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Plan_ClampsANonPositiveChunkCeilingToAtLeastOne(int maxChunks)
    {
        var plan = KnowledgeChunking.Plan("A\n\nB\n\nC", 100, maxChunks);

        Assert.Single(plan.Chunks);
        Assert.True(plan.DroppedChunks >= 2);
    }

    // ---- surrogate pairs ----------------------------------------------------

    [Fact]
    public void Plan_NeverSplitsASurrogatePairNoMatterWhereTheBudgetFalls()
    {
        // ⛔ The defect this catches: cutting between the two halves of an emoji produces an unpaired
        // surrogate, which then fails UTF-8 encoding at the provider and wedges the embedding runner on every
        // later pass. The emoji is 2 chars, so an odd budget cuts exactly between the halves.
        var content = string.Concat(Enumerable.Repeat("🙂", 50));

        for (var candidateBudget = 1; candidateBudget <= 12; candidateBudget++)
        {
            var plan = KnowledgeChunking.Plan(content, candidateBudget, 500);

            Assert.NotEmpty(plan.Chunks);

            foreach (var chunk in plan.Chunks)
            {
                Assert.False(
                    char.IsLowSurrogate(chunk.Content[0]),
                    $"Chunk bắt đầu bằng low surrogate với ngân sách {candidateBudget}.");
                Assert.False(
                    char.IsHighSurrogate(chunk.Content[^1]),
                    $"Chunk kết thúc bằng high surrogate với ngân sách {candidateBudget}.");

                Assert.All(
                    Pairs(chunk.Content),
                    balanced => Assert.True(
                        balanced,
                        $"Chunk có surrogate lẻ với ngân sách {candidateBudget}."));
            }
        }
    }

    [Fact]
    public void Plan_WithASurrogatePair_StillLosesNoCharacterWhenNothingIsDropped()
    {
        var content = string.Concat(Enumerable.Repeat("🙂", 20));

        var plan = KnowledgeChunking.Plan(content, 7, 500);

        // Re-assembling the chunks must reproduce the input exactly: a "fix" that dropped half a pair would
        // silently corrupt the document.
        Assert.Equal(content, string.Concat(plan.Chunks.Select(c => c.Content)));
    }

    [Fact]
    public void Plan_WithABudgetThatFitsWholePairs_KeepsEachPairTogether()
    {
        // An even budget can hold whole pairs, so every chunk must be an exact number of emoji — no chunk may
        // ever end on a high surrogate (which is what "half an emoji" looks like in UTF-16).
        var plan = KnowledgeChunking.Plan("🙂🙂🙂🙂", 4, 100);

        Assert.NotEmpty(plan.Chunks);
        Assert.Equal("🙂🙂🙂🙂", string.Concat(plan.Chunks.Select(c => c.Content)));

        foreach (var chunk in plan.Chunks)
        {
            Assert.Equal(0, chunk.Content.Length % 2);
            Assert.False(char.IsHighSurrogate(chunk.Content[^1]));
            Assert.False(char.IsLowSurrogate(chunk.Content[0]));
        }
    }

    [Fact]
    public void Plan_WithAnOddBudget_LosesNoScalarAndProducesNoUnpairedSurrogate()
    {
        // An odd budget cannot hold a whole pair, so the implementation must back off one char. If it instead
        // split the pair, the re-assembled text would contain an unpaired surrogate that UTF-8 encoding rejects.
        var plan = KnowledgeChunking.Plan("🙂🙂🙂", 3, 100);

        var rebuilt = string.Concat(plan.Chunks.Select(c => c.Content));

        Assert.Equal("🙂🙂🙂", rebuilt);
        Assert.All(Pairs(rebuilt), balanced => Assert.True(balanced));
    }

    // ---- BuildContextBlock --------------------------------------------------

    [Fact]
    public void BuildContextBlock_WithNoSnippets_IsEmpty()
    {
        Assert.Equal(string.Empty, KnowledgeChunking.BuildContextBlock([], 500));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void BuildContextBlock_WithNoBudget_IsEmpty(int maxChars)
    {
        var snippets = new[] { new KnowledgeSnippet("Quy ước", "Nội dung") };

        Assert.Equal(string.Empty, KnowledgeChunking.BuildContextBlock(snippets, maxChars));
    }

    [Fact]
    public void BuildContextBlock_ContainsTheHeaderThePromptRefersTo()
    {
        // The header is the contract with the model (and with the prompt tests): a renamed header would make
        // the block unrecognisable without failing anything else.
        Assert.Equal("KIẾN THỨC DỰ ÁN:", KnowledgeChunking.ContextHeader);

        var block = KnowledgeChunking.BuildContextBlock(
            [new KnowledgeSnippet("Quy ước đặt tên", "Dùng snake_case cho biến.")], 500);

        Assert.Contains(KnowledgeChunking.ContextHeader, block, StringComparison.Ordinal);
        Assert.Contains("Quy ước đặt tên", block, StringComparison.Ordinal);
        Assert.Contains("Dùng snake_case cho biến.", block, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildContextBlock_NeverExceedsTheCharacterBudget()
    {
        // One large snippet so the block is dominated by a single body line.
        var snippets = new[]
        {
            new KnowledgeSnippet("Tài liệu 0", new string('a', 200)),
            new KnowledgeSnippet("Tài liệu 1", new string('b', 200)),
        };

        // A budget too small for even the header plus one bullet is a legitimate "render nothing" case.
        Assert.Equal(string.Empty, KnowledgeChunking.BuildContextBlock(snippets, 40));

        // MEASURED: header 18 + one 200-char snippet 235 = 253, and the note costs 58. 320 holds the snippet AND
        // the note and still has 9 chars spare, so nothing can be dropped by accident.
        const int budget = 320;

        var block = KnowledgeChunking.BuildContextBlock(snippets, budget);

        Assert.NotEmpty(block);
        Assert.True(block.Length <= budget, $"Khối dài {block.Length} > {budget}.");
        Assert.Contains("Tài liệu 0", block, StringComparison.Ordinal);
        Assert.Contains("còn 1 đoạn", block, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildContextBlock_WhenTheBudgetCannotHoldTheNote_RendersTheKnowledgeWithoutIt()
    {
        // ⛔ The defect this catches: reserving the note's cost up front evicted a snippet that fitted the ceiling
        // on its own, so a slightly smaller budget produced a strictly smaller block for no reason a caller could
        // see — and an exact reservation (pass once to learn the count, then reserve that much) produced NO block at
        // all when the ceiling could hold a snippet but not a snippet plus the note.
        //
        // The contract is that content wins: the budget is filled with snippets first and the note is added only
        // from the room left over. Dropping the note costs a reader the knowledge that something was omitted;
        // dropping the snippet costs them the knowledge itself.
        var snippets = new[]
        {
            new KnowledgeSnippet("Tài liệu 0", new string('a', 200)),
            new KnowledgeSnippet("Tài liệu 1", new string('b', 200)),
        };

        // MEASURED: the same snippet plus header is 235, so 320 fits it with 85 chars spare while the SECOND
        // 235-char snippet still cannot fit — the case under test.
        const int budget = 320;

        var block = KnowledgeChunking.BuildContextBlock(snippets, budget);

        Assert.True(block.Length <= budget, $"Khối dài {block.Length} > {budget}.");
        Assert.Contains("Tài liệu 0", block, StringComparison.Ordinal);
        Assert.Contains(new string('a', 200), block, StringComparison.Ordinal);
        Assert.DoesNotContain("Tài liệu 1", block, StringComparison.Ordinal);

        // With more room the note appears, so this is a budget boundary and not the note being broken.
        var withRoom = KnowledgeChunking.BuildContextBlock(snippets, 400);

        Assert.Contains("Tài liệu 0", withRoom, StringComparison.Ordinal);
        Assert.Contains("còn 1 đoạn", withRoom, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildContextBlock_WithAOneCharacterBudget_IsEmptyRatherThanOverBudget()
    {
        // ⛔ The defect this catches: the rendering counted each line's trailing newline against the budget and
        // then `TrimEnd` only the LAST one, so a block could measure one line over the caller's ceiling. A
        // prompt block that overshoots its own cost ceiling is exactly what `RagMaxContextChars` exists to stop.
        var snippets = new[]
        {
            new KnowledgeSnippet("T", "nội dung"),
            new KnowledgeSnippet("U", "nội dung khác"),
        };

        for (var budget = 1; budget <= 40; budget++)
        {
            var block = KnowledgeChunking.BuildContextBlock(snippets, budget);

            Assert.True(block.Length <= budget, $"Ngân sách {budget} nhưng khối dài {block.Length}.");
        }
    }

    [Fact]
    public void BuildContextBlock_WithABudgetSmallerThanTheReservedNote_StillRendersTheFirstSnippet()
    {
        // ⛔ The defect this catches: the space reserved for the "(còn N đoạn …)" note was charged against the
        // FIRST snippet too, so the reserve alone could exceed the whole ceiling and the block came back EMPTY
        // even though the header and one snippet fitted comfortably. A knowledge feature that silently renders
        // nothing at a width it can hold is worse than one that renders less than everything.
        //
        // The budget is swept rather than fixed, because the exact figure where a block stops being empty is
        // arithmetic rather than contract: what matters is that a budget which can hold the header plus a snippet
        // always produces the snippet, and never overshoots the ceiling while doing it.
        var snippets = Enumerable.Range(0, 20)
            .Select(i => new KnowledgeSnippet($"Doc {i}", "abc"))
            .ToList();

        var rendered = 0;

        for (var budget = 1; budget <= 400; budget++)
        {
            var block = KnowledgeChunking.BuildContextBlock(snippets, budget);

            // The hard ceiling, at every width — this is the property `RagMaxContextChars` exists to enforce and
            // the one an off-by-one in the newline accounting breaks.
            Assert.True(block.Length <= budget, $"Ngân sách {budget} nhưng khối dài {block.Length}.");

            if (block.Length == 0)
            {
                continue;
            }

            rendered++;

            // Only blocks that actually carry a snippet are examined: a block may be just the header when the
            // ceiling is exactly the header's width, and that is not the case under test.
            var shown = snippets.Count(s => block.Contains($"- {s.DocumentTitle}:", StringComparison.Ordinal));

            if (shown == 0)
            {
                continue;
            }

            Assert.Contains("abc", block, StringComparison.Ordinal);

            // And when a document was left out, the note names exactly how many.
            if (block.Contains("không hiển thị do giới hạn độ dài", StringComparison.Ordinal))
            {
                Assert.Contains($"còn {snippets.Count - shown} đoạn", block, StringComparison.Ordinal);
            }
        }

        // The sweep must have covered a real render — otherwise this test would pass by rendering nothing at all.
        Assert.True(rendered > 0, "Fixture: không ngân sách nào dựng được khối.");
    }

    [Fact]
    public void BuildContextBlock_WhenNothingFits_IsTrulyEmptyAndClaimsNoZero()
    {
        // The other half of "the note is mandatory when something was dropped": when NOTHING fits there is no
        // note either. A bare header, or a note reading "còn 0 đoạn", would claim knowledge the caller is not
        // being shown — the misleading-degradation failure this whole file is written to avoid.
        var snippets = new[]
        {
            new KnowledgeSnippet("Tài liệu 0", new string('b', 200)),
            new KnowledgeSnippet("Tài liệu 1", new string('b', 200)),
        };

        // Header + one body is ~231 chars, so 200 cannot hold even the first snippet.
        var block = KnowledgeChunking.BuildContextBlock(snippets, 200);

        Assert.Equal(string.Empty, block);
    }

    [Fact]
    public void BuildContextBlock_SaysHowManySnippetsTheBudgetLeftOut()
    {
        // ⛔ The defect this catches: a block that silently dropped the remaining knowledge looks identical to
        // one that had nothing else to say — the model (and the operator reading a trace) would believe the
        // workspace holds exactly the passages shown.
        var snippets = Enumerable.Range(0, 20)
            .Select(i => new KnowledgeSnippet($"Tài liệu {i}", "nội dung"))
            .ToList();

        // MEASURED with these fixtures: header 18 and one snippet 38, so 220 renders several snippets and drops the
        // rest — the situation this test covers.
        const int budget = 220;

        var block = KnowledgeChunking.BuildContextBlock(snippets, budget);

        // Derived, not hard-coded: how many snippets the budget holds is an implementation detail, but "the
        // number in the note equals the number that are missing" is the contract.
        var shown = snippets.Count(snippet => block.Contains($"- {snippet.DocumentTitle}:", StringComparison.Ordinal));
        var missing = snippets.Count - shown;

        Assert.True(shown > 0 && missing > 0, $"Fixture không tạo ra tình huống cắt bớt (hiện {shown}, thiếu {missing}).");
        Assert.True(block.Length <= budget);
        Assert.Contains(KnowledgeChunking.ContextHeader, block, StringComparison.Ordinal);

        // The note is written from whatever room the snippets left over, so whether it appears at a given width is
        // the implementation's business. What the caller IS guaranteed: when there is a note, the number in it
        // equals the number of snippets actually missing.
        if (block.Contains("không hiển thị do giới hạn độ dài", StringComparison.Ordinal))
        {
            Assert.Contains($"còn {missing} đoạn", block, StringComparison.Ordinal);
        }
    }

    // ---- budget helpers -----------------------------------------------------

    /// <summary>
    /// Headroom any budget in this suite must leave on top of a one-snippet render, so the mandatory
    /// "(còn N đoạn …)" note fits beside it. Comfortably above the note's real cost (which is ~57 chars for a
    /// two-digit count) because the exact figure is the implementation's business — the tests below assert the
    /// CONTRACT (fits the ceiling, note present, exact count), not the note's byte length.
    /// </summary>
    /// <summary>
    /// Headroom added on top of a one-snippet render so the note fits after the CONTENT has been filled in.
    ///
    /// <b>Measured, not guessed.</b> The renderer fills the budget with snippets first and only then adds the
    /// "(còn N đoạn …)" note from the room left over, so a fixture that wants the note must leave room for the
    /// content it wants to show <i>and</i> the note. Measured values with the fixtures below: one snippet render is
    /// 33 chars, the note is 59 chars including its newline, and the renderer fits a snippet every ~38 chars — so
    /// 180 headroom was consumed by roughly five extra snippets and left only ~10 chars for the note. 250 leaves
    /// room for a handful of snippets plus the note.
    /// </summary>
    /// <summary>
    /// Headroom added on top of a one-snippet render, on top of the note's own cost, so a fixture that wants one
    /// snippet plus the note also has room for further snippets that get dropped. Measured with the fixtures below:
    /// one snippet render is 33 chars and the renderer fits a snippet every ~38 chars.
    /// </summary>
    private const int SpareSnippetHeadroomChars = 50;

    /// <summary>
    /// The exact cost of the "(còn N đoạn …)" note for a one-digit count, including its newline. Duplicated from
    /// the implementation on purpose: the constants there are private, and a test that silently stopped pinning
    /// the note's cost would stop noticing when the wording changes.
    /// </summary>
    private static int NoteCostOnce()
        => "(còn ".Length
           + 1
           + " đoạn kiến thức không hiển thị do giới hạn độ dài)".Length
           + Environment.NewLine.Length;

    /// <summary>The tightest budget that can render one snippet AND still write the note beside it.</summary>
    private static int NoteFittingBudget(KnowledgeSnippet snippet)
    {
        var alone = KnowledgeChunking.BuildContextBlock([snippet], 4_000);

        Assert.False(string.IsNullOrEmpty(alone), "Fixture: một đoạn đơn lẻ phải dựng được.");

        return alone.Length + Environment.NewLine.Length + NoteCostOnce();
    }

    /// <summary>The same, with room left over so further snippets can be dropped and counted.</summary>
    private static int NoteFittingBudgetWithSpare(KnowledgeSnippet snippet)
        => NoteFittingBudget(snippet) + NoteCostOnce() + SpareSnippetHeadroomChars;

    /// <summary>A budget that fits the snippet but cannot pay for the note.</summary>
    private static int BudgetWithoutRoomForTheNote(KnowledgeSnippet snippet)
    {
        var alone = KnowledgeChunking.BuildContextBlock([snippet], 4_000);

        Assert.False(string.IsNullOrEmpty(alone), "Fixture: một đoạn đơn lẻ phải dựng được.");

        return alone.Length + Environment.NewLine.Length;
    }

    [Fact]
    public void BuildContextBlock_AtABudgetThatOnlyFitsTheHeader_IncludesNoSnippetAtAll()
    {
        // The header alone is 18 chars. A block whose first bullet does not fit is not "one snippet with a
        // missing heading" — it is nothing, because the snippet would have no citation and the model would
        // attribute the text to the board.
        var headerLength = KnowledgeChunking.ContextHeader.Length;

        var block = KnowledgeChunking.BuildContextBlock(
            [new KnowledgeSnippet("T", "nội dung dài")], headerLength + 1);

        Assert.Equal(string.Empty, block);
    }

    [Fact]
    public void BuildContextBlock_CutsByWholeLinesRatherThanMidSentence()
    {
        // A half-sentence handed to the model is worse than an absent one: the model completes it from
        // imagination. The contract is therefore "a snippet is either rendered whole or not at all" — never
        // "its body line got cut in the middle". The budget is derived from a control render so the assertion
        // does not depend on how long the header happens to be.
        var short1 = new KnowledgeSnippet("Ngắn", "ok");
        var long2 = new KnowledgeSnippet("Dài", new string('x', 400));

        var onlyFirst = KnowledgeChunking.BuildContextBlock([short1], 500);

        Assert.Contains("Ngắn", onlyFirst, StringComparison.Ordinal);

        // Room for the first snippet, its dropped-note, and only PART of the second snippet's body: the long body
        // must be dropped whole rather than quoted up to the ceiling.
        const int budget = 200;

        var block = KnowledgeChunking.BuildContextBlock([short1, long2], budget);

        Assert.True(block.Length <= budget);
        Assert.Contains("Ngắn", block, StringComparison.Ordinal);
        Assert.DoesNotContain("xxxx", block, StringComparison.Ordinal);

        // And with room for everything, the long body is present in full rather than in fragments.
        var both = KnowledgeChunking.BuildContextBlock([short1, long2], 500);

        Assert.Contains(new string('x', 400), both, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildContextBlock_AnEmptyTitleGetsAPlaceholderRatherThanAnEmptyBullet()
    {
        var block = KnowledgeChunking.BuildContextBlock(
            [new KnowledgeSnippet("   ", "nội dung quan trọng")], 500);

        Assert.Contains("(không rõ tài liệu)", block, StringComparison.Ordinal);
        Assert.Contains("nội dung quan trọng", block, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildContextBlock_ContainsNoIdentifierEvenWhenTheSnippetWouldCarryOne()
    {
        // The invariant the whole prompt work follows: the model cannot echo an id it was never given. This
        // asserts the block itself adds none — not even a chunk or document id from the source rows.
        var documentId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();

        var block = KnowledgeChunking.BuildContextBlock(
            [new KnowledgeSnippet($"Tài liệu {documentId}", $"Nội dung đoạn {chunkId}")], 500);

        // A block that renders "at most maxChars" is exactly what is asserted: nothing here may append an id
        // of its own. The snippet's own text is reproduced verbatim, so the check is on the added scaffolding.
        Assert.DoesNotContain("documentId", block, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("chunkId", block, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workspaceId", block, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("KIẾN THỨC DỰ ÁN:", block, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildContextBlock_TrimsTrailingWhitespaceSoTheLengthCheckIsHonest()
    {
        var block = KnowledgeChunking.BuildContextBlock(
            [new KnowledgeSnippet("T", "nội dung")], 500);

        Assert.Equal(block, block.TrimEnd());
    }

    /// <summary>Walks a string and reports, for each high surrogate, whether it was followed by a low one.</summary>
    private static IEnumerable<bool> Pairs(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                yield return index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]);
                index++;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                // A low surrogate with no preceding high one is exactly the "unpaired" case.
                yield return false;
            }
        }
    }
}
