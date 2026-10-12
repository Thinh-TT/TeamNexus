using TeamNexus.Modules.Ai.Services;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Phase 14.6 §A4 — the Lightweight Board Digest, and the prompt rules that make it usable.
/// <para>
/// <b>What only this suite can prove.</b> The digest exists to make "how is Sprint 1 going?" answerable when
/// nothing is attached, and the risk it introduces is a <i>cost</i> risk: a board with hundreds of cards must
/// not turn one question into the most expensive request in the product. The character ceiling is therefore
/// asserted directly, on a digest big enough to hit it — an integration test would only ever see small
/// fixtures and would pass with the ceiling removed.
/// </para>
/// </summary>
public sealed class AgentChatDigestTests
{
    private static readonly DateTimeOffset Due = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DIGEST1_CountsAndTopTasksAreRenderedWithDisplayNamesOnly()
    {
        var digest = new AgentChatDigest(
            [new AgentChatDigestColumn("Cần làm", 5), new AgentChatDigestColumn("Đang làm", 2)],
            [new AgentChatDigestTask("Sửa lỗi đăng nhập", "Đang làm", "Urgent", Due, "Linh")],
            TotalTasks: 7,
            MaxChars: 2000);

        var block = AgentChatPrompts.BuildDigestBlock(digest);

        Assert.Contains(AgentChatPrompts.DigestHeader, block, StringComparison.Ordinal);
        Assert.Contains("Tổng số: 7 thẻ", block, StringComparison.Ordinal);
        Assert.Contains("* Cần làm: 5", block, StringComparison.Ordinal);
        Assert.Contains("* Đang làm: 2", block, StringComparison.Ordinal);
        Assert.Contains("[Urgent] Sửa lỗi đăng nhập — Đang làm", block, StringComparison.Ordinal);
        Assert.Contains("Linh", block, StringComparison.Ordinal);

        // No identifier may appear: the digest is prompt context, but the model copies what it is given, and
        // §A2 exists precisely because a copied Guid ended up in a user-facing sentence.
        Assert.False(AiChatAnswerSanitizer.ContainsIdentifier(block));
    }

    [Fact]
    public void DIGEST2_BlankFieldsReadAsWordsNotAsEmptySlots()
    {
        var digest = new AgentChatDigest(
            [new AgentChatDigestColumn("Cần làm", 1)],
            [new AgentChatDigestTask("Thẻ chưa ai nhận", "Cần làm", null, null, null)],
            TotalTasks: 1,
            MaxChars: 2000);

        var block = AgentChatPrompts.BuildDigestBlock(digest);

        Assert.Contains("(chưa đặt)", block, StringComparison.Ordinal);
        Assert.Contains("không hạn", block, StringComparison.Ordinal);
        Assert.Contains("chưa gán", block, StringComparison.Ordinal);
    }

    [Fact]
    public void DIGEST3_TheCharacterCeilingIsNeverExceeded()
    {
        // 30 long titles against a 400-character ceiling: the whole point of the budget is that this case
        // cannot blow up, and the cut must fall between lines rather than inside a title.
        var tasks = Enumerable.Range(1, 30)
            .Select(i => new AgentChatDigestTask(
                $"Thẻ số {i} với tiêu đề dài để chắc chắn vượt trần ký tự của bản tóm tắt",
                "Cần làm",
                "High",
                Due,
                "Linh"))
            .ToList();

        var digest = new AgentChatDigest(
            [new AgentChatDigestColumn("Cần làm", 30)],
            tasks,
            TotalTasks: 30,
            MaxChars: 400);

        var block = AgentChatPrompts.BuildDigestBlock(digest);

        Assert.True(block.Length <= 400, $"Bản tóm tắt dài {block.Length} ký tự, vượt trần 400.");

        // Whatever survived must be whole lines: a half-written title would mislead the model more than an
        // omitted one. Every task line that is present has to carry all of its fields.
        var taskLines = block
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Contains("Thẻ số", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(taskLines);

        foreach (var line in taskLines)
        {
            Assert.Contains("bản tóm tắt — Cần làm", line, StringComparison.Ordinal);
            Assert.EndsWith("· Linh", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DIGEST4_AnEmptyOrAbsentDigestProducesNoBlock()
    {
        Assert.Equal(string.Empty, AgentChatPrompts.BuildDigestBlock(null));
        Assert.Equal(
            string.Empty,
            AgentChatPrompts.BuildDigestBlock(new AgentChatDigest([], [], TotalTasks: 0, MaxChars: 2000)));

        // A ceiling too small to hold even the heading means "say nothing", never "say half a heading".
        Assert.Equal(
            string.Empty,
            AgentChatPrompts.BuildDigestBlock(new AgentChatDigest(
                [new AgentChatDigestColumn("Cần làm", 1)],
                [],
                TotalTasks: 1,
                MaxChars: 1)));
    }

    [Fact]
    public void DIGEST5_TheDigestIsOnlyIncludedWhenNothingIsAttached()
    {
        var digest = new AgentChatDigest(
            [new AgentChatDigestColumn("Cần làm", 5)],
            [new AgentChatDigestTask("Sửa lỗi đăng nhập", "Cần làm", "Urgent", Due, "Linh")],
            TotalTasks: 5,
            MaxChars: 2000);

        var withoutAttachments = new AgentChatBoardContext(
            "Sprint 1", null, [], [], [], digest);
        var withAttachments = new AgentChatBoardContext(
            "Sprint 1",
            null,
            [],
            [new AgentChatContextTask(Guid.NewGuid(), "Thẻ đang gắn", "Cần làm", null, null, null, null, [])],
            [],
            digest);

        var emptyBlock = AgentChatPrompts.BuildContextBlock(withoutAttachments);
        var attachedBlock = AgentChatPrompts.BuildContextBlock(withAttachments);

        // `DigestBlockMarker`, not the bare heading: rule 7 of the system prompt quotes the heading by name, so
        // asserting on that alone would pass even with the digest gone.
        Assert.Contains(AgentChatPrompts.DigestBlockMarker, emptyBlock, StringComparison.Ordinal);
        Assert.Contains("không có thẻ nào", emptyBlock, StringComparison.Ordinal);

        // With an attachment list the model already has what it needs; board-wide statistics on top would
        // spend tokens on something the user did not ask about.
        Assert.DoesNotContain(AgentChatPrompts.DigestBlockMarker, attachedBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void DIGEST6_ThePromptTellsTheModelToUseTheDigestAndNeverToPrintIds()
    {
        // The TEMPLATE, not a rendered prompt: the rendered one legitimately carries real column/member ids
        // (the model needs them to propose a move or an assignee), so the property under test is that the
        // rules themselves contain no Guid — a negative example written with a real one is exactly what a model
        // copies verbatim.
        Assert.False(AiChatAnswerSanitizer.ContainsIdentifier(AgentChatPrompts.SystemPromptTemplate));

        var prompt = AgentChatPrompts.BuildSystemPrompt("(ngữ cảnh)");

        // §A2: the prose rule, stated in the prompt as well as enforced in code.
        Assert.Contains("KHÔNG hiển thị raw ID, GUID hoặc UUID", prompt, StringComparison.Ordinal);

        // §A4: the model must not answer "I have no data" while the digest is sitting in its context.
        Assert.Contains(AgentChatPrompts.DigestHeader, prompt, StringComparison.Ordinal);
        Assert.Contains("không được nói rằng bạn không có dữ liệu", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void DIGEST7_TheTaskChatPromptAlsoForbidsRawIds()
    {
        Assert.False(AiChatAnswerSanitizer.ContainsIdentifier(AiChatPrompts.SystemPromptTemplate));

        var prompt = AiChatPrompts.BuildSystemPrompt("(ngữ cảnh)");

        Assert.Contains("KHÔNG hiển thị raw ID, GUID hoặc UUID", prompt, StringComparison.Ordinal);
    }
}
