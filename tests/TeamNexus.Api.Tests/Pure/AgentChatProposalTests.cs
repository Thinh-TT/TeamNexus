using System.Text.Json;
using TeamNexus.Modules.Ai.Services;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Phase 14.5 §B — the board-chat proposal parser and validator (CHATPROP-1…CHATPROP-20).
/// <para>
/// Pure by construction: strings in, records out. No database, no HTTP, no provider, no clock. That is
/// what makes the awkward cases affordable to assert <b>exactly</b> — a proposal naming a task from
/// another board, a numeric priority that <c>Enum.TryParse</c> would silently swallow (the Phase 10 BUG-1
/// trap), a timestamp carrying a +07:00 offset (the BUG-2 Npgsql trap), two proposals in one answer —
/// instead of settling for the comfortable happy path.
/// </para>
/// <para>
/// <b>Why these rules matter more than most.</b> The model is untrusted input. Everything below is the
/// boundary that decides whether a Manager is asked to approve something the system actually supports.
/// </para>
/// </summary>
public sealed class AgentChatProposalTests
{
    private static readonly Guid AttachedTask = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTask = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TodoColumn = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DoneColumn = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Member = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Stranger = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static readonly Guid[] Attached = [AttachedTask];
    private static readonly Guid[] Members = [Member];

    private static readonly AgentChatContextColumn[] Columns =
    [
        new(TodoColumn, "Cần làm", IsDone: false),
        new(DoneColumn, "Hoàn thành", IsDone: true),
    ];

    // ---- parser ------------------------------------------------------------

    [Fact]
    public void CHATPROP1_ParserSeparatesTextFromTheProposalBlock()
    {
        var answer = "Tôi thấy thẻ này nên chuyển cột.\n\n```json\n{\"proposal\":{\"kind\":\"MoveTaskToColumn\"}}\n```";

        var parsed = AgentChatProposalParser.Parse(answer);

        Assert.Equal("Tôi thấy thẻ này nên chuyển cột.", parsed.VisibleText);
        // The envelope is unwrapped here, once: the validator works on the proposal itself.
        Assert.Equal("{\"kind\":\"MoveTaskToColumn\"}", parsed.JsonBlock);
    }

    [Fact]
    public void CHATPROP2_ParserTakesTheLastBlockNotTheFirst()
    {
        // A model that shows an EXAMPLE block while explaining must not have the example submitted: the
        // prompt asks for the explanation first and the real proposal last.
        var answer = "Ví dụ định dạng:\n```json\n{\"proposal\":{\"kind\":\"Example\"}}\n```\n"
                     + "Còn đây là đề xuất thật:\n```json\n{\"proposal\":{\"kind\":\"MoveTaskToColumn\"}}\n```";

        var parsed = AgentChatProposalParser.Parse(answer);

        Assert.Contains("MoveTaskToColumn", parsed.JsonBlock);
        Assert.DoesNotContain("Example", parsed.JsonBlock);

        // The EARLIER block stays visible: it was explanatory prose, not the proposal, and silently
        // deleting text the model wrote would be worse than showing it. Only the last block is consumed.
        Assert.Contains("Ví dụ định dạng:", parsed.VisibleText);
        Assert.Contains("{\"kind\":\"Example\"}", parsed.VisibleText);
        Assert.Contains("Còn đây là đề xuất thật:", parsed.VisibleText);
    }

    [Fact]
    public void CHATPROP3_ParserWithoutABlockLeavesTheAnswerIntact()
    {
        var parsed = AgentChatProposalParser.Parse("Chỉ là câu trả lời bình thường.");

        Assert.Equal("Chỉ là câu trả lời bình thường.", parsed.VisibleText);
        Assert.Null(parsed.JsonBlock);
    }

    [Fact]
    public void CHATPROP4_ParserIsSilentOnAnUnterminatedOrEmptyFence()
    {
        // The model was cut off mid-block, and the fence with nothing in it: neither may throw, because a
        // malformed answer must still be readable to the user.
        var unterminated = AgentChatProposalParser.Parse("Giải thích trước\n```json\n{\"proposal\":{");
        Assert.Null(unterminated.JsonBlock);
        Assert.Contains("Giải thích trước", unterminated.VisibleText);

        var empty = AgentChatProposalParser.Parse("Không có gì\n```json\n\n```");
        Assert.Null(empty.JsonBlock);

        var blank = AgentChatProposalParser.Parse("   ");
        Assert.Equal(string.Empty, blank.VisibleText);
        Assert.Null(blank.JsonBlock);
    }

    // ---- validator: accept ------------------------------------------------

    [Fact]
    public void CHATPROP5_AMoveOnAnAttachedTaskIsNormalized()
    {
        var json = MoveProposal(AttachedTask, TodoColumn);

        var proposal = Normalize(json, out var error);

        Assert.Null(error);
        Assert.NotNull(proposal);
        Assert.Equal(AgentChatProposalKinds.MoveTaskToColumn, proposal!.Kind);
        Assert.Equal(AttachedTask, proposal.Payload.TaskId);
        Assert.Equal(TodoColumn, proposal.Payload.ColumnId);
    }

    [Fact]
    public void CHATPROP6_AnUpdateKeepsOnlyTheFieldsTheModelActuallySent()
    {
        var json = """
        {"proposal":{"kind":"UpdateTaskFields","summary":"Đổi ưu tiên",
          "payload":{"taskId":"11111111-1111-1111-1111-111111111111","priority":"urgent"}}}
        """;

        var proposal = Normalize(json, out var error);

        Assert.Null(error);
        Assert.Equal("Urgent", proposal!.Payload.Priority);
        Assert.Null(proposal.Payload.Title);
        Assert.Null(proposal.Payload.Description);
        Assert.Null(proposal.Payload.DueDate);
        Assert.Null(proposal.Payload.AssigneeId);
    }

    [Fact]
    public void CHATPROP7_CreateSubtasksBuildsTheSmartSetupShapeTheApplierReads()
    {
        var json = """
        {"proposal":{"kind":"CreateSubtasks","summary":"Chia việc",
          "payload":{"columnId":"33333333-3333-3333-3333-333333333333",
            "tasks":[{"title":"Viết tài liệu","priority":"High","assigneeId":"55555555-5555-5555-5555-555555555555"}]}}}
        """;

        var proposal = Normalize(json, out var error);

        Assert.Null(error);
        Assert.NotNull(proposal!.Payload.Tasks);
        var task = Assert.Single(proposal.Payload.Tasks!);
        Assert.Equal("Viết tài liệu", task.Title);
        Assert.Equal("High", task.Priority);
        // The assignee is resolved against the member set, so Matched is true and the id survived.
        Assert.Equal(Member, task.Assignee!.UserId);
        Assert.True(task.Assignee.Matched);
    }

    [Fact]
    public void CHATPROP8_ADueDateWithAnOffsetIsNormalizedToUtc()
    {
        // The BUG-2 trap: Npgsql throws "only offset 0 (UTC) is supported" from SaveChangesAsync, and a
        // model is exactly where a +07:00 timestamp comes from.
        var json = """
        {"proposal":{"kind":"UpdateTaskFields","summary":"Dời hạn",
          "payload":{"taskId":"11111111-1111-1111-1111-111111111111","dueDate":"2026-03-01T17:00:00+07:00"}}}
        """;

        var proposal = Normalize(json, out var error);

        Assert.Null(error);
        Assert.Equal(TimeSpan.Zero, proposal!.Payload.DueDate!.Value.Offset);
        Assert.Equal(new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc), proposal.Payload.DueDate.Value.UtcDateTime);
    }

    [Fact]
    public void CHATPROP9_AnAssigneeOutsideTheWorkspaceIsDroppedNotRejected()
    {
        var json = """
        {"proposal":{"kind":"UpdateTaskFields","summary":"Giao việc",
          "payload":{"taskId":"11111111-1111-1111-1111-111111111111","assigneeId":"66666666-6666-6666-6666-666666666666"}}}
        """;

        var proposal = Normalize(json, out var error);

        // The proposal itself is fine; only the unresolvable assignee is dropped — the human is reviewing
        // the card anyway, and "assigned to nobody" is a safe outcome.
        Assert.Null(error);
        Assert.Null(proposal!.Payload.AssigneeId);
    }

    [Fact]
    public void CHATPROP10_KindMatchingIsCaseInsensitiveButStoredCanonical()
    {
        var json = """
        {"proposal":{"kind":"movetasktocolumn","summary":"x",
          "payload":{"taskId":"11111111-1111-1111-1111-111111111111","columnId":"33333333-3333-3333-3333-333333333333"}}}
        """;

        var proposal = Normalize(json, out var error);

        Assert.Null(error);
        Assert.Equal(AgentChatProposalKinds.MoveTaskToColumn, proposal!.Kind);
    }

    [Fact]
    public void CHATPROP11_AMissingSummaryGetsAReadablePlaceholder()
    {
        var proposal = Normalize(MoveProposal(AttachedTask, TodoColumn, summary: null), out var error);

        Assert.Null(error);
        Assert.False(string.IsNullOrWhiteSpace(proposal!.Summary));
    }

    // ---- validator: reject ------------------------------------------------

    [Fact]
    public void CHATPROP12_ATaskThatWasNotAttachedIsRejected()
    {
        // The single most important rule in the class: without it a Manager could approve a change to a
        // card on a board they were never looking at.
        var proposal = Normalize(MoveProposal(OtherTask, TodoColumn), out var error);

        Assert.Null(proposal);
        Assert.Contains("không được gắn", error);
    }

    [Fact]
    public void CHATPROP13_AColumnOfAnotherBoardIsRejected()
    {
        var foreign = Guid.NewGuid();

        var proposal = Normalize(MoveProposal(AttachedTask, foreign), out var error);

        Assert.Null(proposal);
        Assert.Contains("không thuộc bảng này", error);
    }

    [Fact]
    public void CHATPROP14_AnUnknownKindIsRejected()
    {
        var json = """
        {"proposal":{"kind":"DeleteEverything","summary":"x","payload":{}}}
        """;

        var proposal = Normalize(json, out var error);

        Assert.Null(proposal);
        Assert.Contains("không được hỗ trợ", error);
    }

    [Fact]
    public void CHATPROP15_ANumericPriorityIsRejectedRatherThanSilentlyMapped()
    {
        // Phase 10 BUG-1: `Enum.TryParse("1")` succeeds and maps to an ordinal — here that would turn a
        // model typo into a silent "Medium". It must be a 400 instead.
        var json = """
        {"proposal":{"kind":"UpdateTaskFields","summary":"x",
          "payload":{"taskId":"11111111-1111-1111-1111-111111111111","priority":"1"}}}
        """;

        var proposal = Normalize(json, out var error);

        Assert.Null(proposal);
        Assert.Contains("ưu tiên", error);
    }

    [Fact]
    public void CHATPROP16_MalformedJsonIsRejectedWithAVietnameseReason()
    {
        var proposal = Normalize("{not valid json", out var error);

        Assert.Null(proposal);
        Assert.Contains("JSON", error);
    }

    [Fact]
    public void CHATPROP17_AnUpdateThatChangesNothingIsRejected()
    {
        var json = """
        {"proposal":{"kind":"UpdateTaskFields","summary":"x","payload":{"taskId":"11111111-1111-1111-1111-111111111111"}}}
        """;

        var proposal = Normalize(json, out var error);

        Assert.Null(proposal);
        Assert.Contains("không thay đổi trường nào", error);
    }

    [Fact]
    public void CHATPROP18_CreateSubtasksWithoutUsableTitlesIsRejected()
    {
        var json = """
        {"proposal":{"kind":"CreateSubtasks","summary":"x","payload":{"columnId":"33333333-3333-3333-3333-333333333333",
          "tasks":[{"title":"   "},{"title":null}]}}}
        """;

        var proposal = Normalize(json, out var error);

        Assert.Null(proposal);
        Assert.Contains("không có thẻ nào hợp lệ", error);
    }

    [Fact]
    public void CHATPROP19_CreatedTasksAreCappedAtTheConfiguredCeiling()
    {
        var tasks = string.Join(",", Enumerable.Range(1, 30).Select(i => $"{{\"title\":\"Thẻ {i}\"}}"));

        var json = """
        {"proposal":{"kind":"CreateSubtasks","summary":"x","payload":{"columnId":"33333333-3333-3333-3333-333333333333",
          "tasks":[TASKS_PLACEHOLDER]}}}
        """.Replace("TASKS_PLACEHOLDER", tasks, StringComparison.Ordinal);

        var proposal = Normalize(json, out var error, maxTasks: 5);

        Assert.Null(error);
        Assert.Equal(5, proposal!.Payload.Tasks!.Count);
    }

    [Fact]
    public void CHATPROP20_MissingPayloadAndMissingKindAreBothRejected()
    {
        Assert.Null(Normalize("""{"proposal":{"kind":"MoveTaskToColumn","summary":"x"}}""", out var noPayload));
        Assert.Contains("payload", noPayload);

        Assert.Null(Normalize("""{"proposal":{"summary":"x","payload":{}}}""", out var noKind));
        Assert.Contains("kind", noKind);
    }

    // ---- helpers ----------------------------------------------------------

    private static AgentChatProposal? Normalize(string json, out string? error, int maxTasks = 20)
        => AgentChatProposalValidator.Normalize(json, Attached, Columns, Members, maxTasks, out error);

    private static string MoveProposal(Guid taskId, Guid columnId, string? summary = "Chuyển thẻ")
        => JsonSerializer.Serialize(new
        {
            proposal = new
            {
                kind = AgentChatProposalKinds.MoveTaskToColumn,
                summary,
                payload = new { taskId, columnId },
            },
        });
}
