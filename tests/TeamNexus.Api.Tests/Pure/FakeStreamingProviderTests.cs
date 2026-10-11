using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Ai.Services;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Phase 14.5 §B — a regression guard for a real defect found while building the board chat.
/// <para>
/// <b>The bug:</b> both chat services checked <c>chunk.Done</c> <i>before</i> appending <c>chunk.Delta</c>.
/// The offline provider puts text in the same chunk that terminates the stream, so that text was silently
/// dropped: every answer came back one chunk short, and the documented invariant
/// <c>done.answer == Σ delta</c> — which the client relies on to recover a lost frame — was quietly false.
/// The Phase 14 suite could not catch it because it only asserted the two were <i>both</i> present, never
/// that they were equal (and the offline fake is the only provider whose last chunk carries text).
/// </para>
/// <para>
/// <b>Why the assertion lives here, at the provider:</b> it pins the exact behaviour the two services must
/// tolerate — "a terminating chunk may carry text" — without an HTTP round trip, so a future provider or
/// a future refactor has one obvious place that says what the contract is.
/// </para>
/// </summary>
public sealed class FakeStreamingProviderTests
{
    [Fact]
    public async Task FAKESTREAM1_TheTerminatingChunkCarriesText()
    {
        var taskId = Guid.Parse("9a274efc-2b47-43a2-be8d-6cc964a1f7a0");
        var columnId = Guid.Parse("1663566c-f892-496c-82b0-389a1e015d88");

        var context = "- Bảng: Demo\n"
                      + "- Cột (theo thứ tự, id — tên — hoàn thành?):\n"
                      + $"  * {columnId} — Todo — không\n"
                      + "- Thẻ đang gắn (1):\n"
                      + $"  * [{taskId}] Chuẩn bị báo cáo";

        var prompt = AgentChatPrompts.BuildSystemPrompt(context);
        var provider = new FakeAiProvider(
            Microsoft.Extensions.Options.Options.Create(new DeepSeekOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FakeAiProvider>.Instance);

        var chunks = new List<AiStreamChunk>();

        await foreach (var chunk in provider.StreamAsync(
                           new AiStreamRequest(prompt, [new AiChatMessage("user", "hỏi")], 0.3, 100)))
        {
            chunks.Add(chunk);
        }

        var terminating = chunks.Where(c => c.Done).ToList();

        // Exactly one terminator, and it carries text — the property the services must handle.
        var single = Assert.Single(terminating);
        Assert.True(
            single.Delta is { Length: > 0 },
            "The fake's terminating chunk must carry text, otherwise this guard proves nothing.");

        // The full answer (every delta, including the terminator's) is a parseable proposal — which is
        // what a service that appends before checking `Done` produces.
        var fullAnswer = string.Concat(chunks.Select(c => c.Delta ?? string.Empty));
        Assert.NotNull(AgentChatProposalParser.Parse(fullAnswer).JsonBlock);

        // And the answer is SHORT if the terminator's text is dropped — the exact defect, stated as a
        // property so a future reader can see why the assertion above matters.
        var withoutTerminator = string.Concat(chunks.Where(c => !c.Done).Select(c => c.Delta ?? string.Empty));
        Assert.True(
            withoutTerminator.Length < fullAnswer.Length,
            "Dropping the terminating chunk's text must change the answer; otherwise nothing to guard.");
    }
}
