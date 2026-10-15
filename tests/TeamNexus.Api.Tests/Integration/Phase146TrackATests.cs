using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 14.6 Track A — the four frictions found by live testing on <c>app.teamnexus.cloud</c>.
/// <list type="bullet">
///   <item><b>A2</b> — a raw Guid must never reach a durable or user-visible chat turn.</item>
///   <item><b>A3</b> — the task chat's transcript lives in <c>agent_chat_messages</c> under <c>task_id</c>,
///   and stays invisible to the board's conversation.</item>
///   <item><b>A4</b> — with nothing attached, the prompt still carries a lightweight board digest.</item>
/// </list>
/// <para>
/// <b>Why the prompt is asserted through the service and not through HTTP.</b> The digest and the prompt rules
/// are inputs to the model, and no frame echoes them back — an HTTP-only test could prove nothing about either.
/// Resolving <see cref="IAgentChatService"/> from the scenario's own host is what lets the <i>composed prompt</i>
/// be the assertion target while still running against the real database and the real DI graph.
/// </para>
/// </summary>
public sealed class Phase146TrackATests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public Phase146TrackATests(DatabaseFixture database)
    {
        _database = database;
    }

    // =====================================================================
    // A3 — the task transcript
    // =====================================================================

    [Fact]
    public async Task PHASE146A_A1_TheTaskChatStoresItsTurnsUnderTaskIdAndLoadsThemBack()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        await SendTaskChatAsync(client, seed.Task.Id, "Thẻ này nên làm gì tiếp?");

        // Exactly the pair: one user turn and one assistant turn. A transcript that silently wrote one row,
        // or three, would still "load back" and still look plausible in the UI.
        List<AgentChatMessage> rows = [];
        await scenario.WithDbContextAsync(async db =>
        {
            rows = await db.AgentChatMessages
                .AsNoTracking()
                .Where(m => m.TaskId == seed.Task.Id)
                .ToListAsync();
        });

        // Asserted as a SET, not a sequence: the exchange's two rows share one `created_at` (the audit stamping
        // captures a single timestamp per SaveChanges), so their stored order is decided by the read rule
        // rather than by the table — and the read rule is asserted over HTTP a few lines below.
        Assert.Equal(2, rows.Count);

        var userRow = Assert.Single(rows, row => row.Role == AgentChatRole.User);
        var assistantRow = Assert.Single(rows, row => row.Role == AgentChatRole.Assistant);

        Assert.Equal("Thẻ này nên làm gì tiếp?", userRow.Content);
        Assert.False(string.IsNullOrWhiteSpace(assistantRow.Content));

        // ⚠️ The scope rule, in the database rather than in a response body: a task turn carries a task id and
        // NO board id, which is what the `ck_agent_chat_messages_scope` CHECK from Migration 11 enforces.
        Assert.All(rows, row => Assert.Null(row.BoardId));
        Assert.All(rows, row => Assert.Equal(seed.Workspace.Id, row.WorkspaceId));
        Assert.Equal(0, await scenario.CountAsync<AgentChatMessage>(m => m.BoardId != null));

        // And it comes back through the API in conversation order.
        //
        // ⚠️ This assertion is the one that caught a REAL defect (fixed in `AgentChatTranscriptQuery`): both
        // rows of an exchange are written in a single SaveChanges, so they share one `created_at` and the old
        // tie-break — a random Guid — put the answer above its question about half the time. Phase 14.5's board
        // transcript had the same latent bug and its own suite had simply been passing by luck.
        var stored = await client.GetJsonAsync<List<JsonElement>>(
            $"/api/tasks/{seed.Task.Id}/agent-chat/messages");

        Assert.NotNull(stored);
        Assert.Equal(2, stored!.Count);
        Assert.Equal("user", stored[0].GetProperty("role").GetString());
        Assert.Equal("assistant", stored[1].GetProperty("role").GetString());
        Assert.Equal("Thẻ này nên làm gì tiếp?", stored[0].GetProperty("content").GetString());
        Assert.NotEmpty(stored[1].GetProperty("content").GetString() ?? string.Empty);

        // A second call is idempotent: reading a transcript must never create one.
        var again = await client.GetJsonAsync<List<JsonElement>>(
            $"/api/tasks/{seed.Task.Id}/agent-chat/messages");

        Assert.Equal(2, again!.Count);
        Assert.Equal(2, await scenario.CountAsync<AgentChatMessage>());
    }

    [Fact]
    public async Task PHASE146A_A2_TheTwoTranscriptsNeverSeeEachOther()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        await SendTaskChatAsync(client, seed.Task.Id, "Câu hỏi ở tab thẻ");
        await SendBoardChatAsync(client, seed.Board.Id, seed.Task.Id, "Câu hỏi ở panel bảng");

        var taskTranscript = await client.GetJsonAsync<List<JsonElement>>(
            $"/api/tasks/{seed.Task.Id}/agent-chat/messages");
        var boardTranscript = await client.GetJsonAsync<List<JsonElement>>(
            $"/api/boards/{seed.Board.Id}/agent-chat/messages");

        // Two rows each, and neither contains the other's question. Without this, "merge the two transcripts"
        // would mean "one conversation mysteriously carries the other's turns".
        Assert.Equal(2, taskTranscript!.Count);
        Assert.Equal(2, boardTranscript!.Count);

        Assert.Contains(
            taskTranscript,
            m => (m.GetProperty("content").GetString() ?? string.Empty).Contains("tab thẻ", StringComparison.Ordinal));
        Assert.DoesNotContain(
            taskTranscript,
            m => (m.GetProperty("content").GetString() ?? string.Empty).Contains("panel bảng", StringComparison.Ordinal));

        Assert.Contains(
            boardTranscript,
            m => (m.GetProperty("content").GetString() ?? string.Empty).Contains("panel bảng", StringComparison.Ordinal));
        Assert.DoesNotContain(
            boardTranscript,
            m => (m.GetProperty("content").GetString() ?? string.Empty).Contains("tab thẻ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PHASE146A_A3_TheTaskTranscriptIsMembersOnlyAndUnknownTasksAre404()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        var outsider = await scenario.CreateUserAsync("Người ngoài");
        using var outsiderClient = await scenario.AsUserAsync(outsider);

        // 404, not 403: the endpoint must not confirm that this task exists (the rule everywhere else).
        var forbidden = await outsiderClient.GetAsync(
            $"/api/tasks/{seed.Task.Id}/agent-chat/messages");

        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);

        using var client = await scenario.AsUserAsync(seed.User);

        var missing = await client.GetAsync(
            $"/api/tasks/{Guid.NewGuid()}/agent-chat/messages");

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task PHASE146A_A4_TaskTranscriptPagingUsesTheBeforeCursor()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        await SendTaskChatAsync(client, seed.Task.Id, "Lượt một");
        await SendTaskChatAsync(client, seed.Task.Id, "Lượt hai");

        // Two exchanges, in reading order: question, answer, question, answer. This is the multi-exchange half
        // of the ordering rule — distinct requests get distinct timestamps, so the pages below can be compared
        // by time, while the pair inside one exchange is ordered by role.
        var whole = await client.GetJsonAsync<List<JsonElement>>(
            $"/api/tasks/{seed.Task.Id}/agent-chat/messages");

        Assert.Equal(
            ["user", "assistant", "user", "assistant"],
            whole!.Select(m => m.GetProperty("role").GetString() ?? string.Empty).ToArray());

        var firstPage = await client.GetJsonAsync<List<JsonElement>>(
            $"/api/tasks/{seed.Task.Id}/agent-chat/messages?take=2");

        Assert.NotNull(firstPage);
        Assert.Equal(2, firstPage!.Count);

        // Strictly older than the oldest row of the first page, so the two pages cannot overlap — the same
        // keyset contract as the board transcript.
        var cursor = firstPage[0].GetProperty("createdAt").GetDateTimeOffset();
        var olderPage = await client.GetJsonAsync<List<JsonElement>>(
            $"/api/tasks/{seed.Task.Id}/agent-chat/messages?take=2&before={Uri.EscapeDataString(cursor.ToString("O"))}");

        Assert.NotNull(olderPage);
        Assert.Equal(2, olderPage!.Count);
        Assert.All(
            olderPage,
            m => Assert.True(m.GetProperty("createdAt").GetDateTimeOffset() < cursor));
    }

    // =====================================================================
    // A2 — the identifier leak
    // =====================================================================

    [Fact]
    public async Task PHASE146A_A5_AnIdentifierTheModelLeaksNeverReachesTheStoredTranscript()
    {
        // A scripted provider that does exactly what the deployed model did: it prints a raw column Guid into
        // the prose. The offline FakeAiProvider cannot produce this, which is why the leak survived Phase 14.5.
        var leakedId = Guid.NewGuid();

        var leakedAnswer =
            $"Sau khi có thẻ trong ngữ cảnh, mình sẽ đề xuất chuyển sang cột \"process\" (id: {leakedId}).";

        var scripted = new ScriptedAiProvider
        {
            StreamChunks = [leakedAnswer],
        };

        await using var scenario = await _database.CreateScenarioAsync(scriptedAi: scripted);
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var frames = await SendBoardChatAsync(client, seed.Board.Id, seed.Task.Id, "Nên chuyển thẻ này đi đâu?");
        var answer = frames.Single(f => f.Event == AiChatSseWriter.DoneEvent).Data
            .GetProperty("answer").GetString() ?? string.Empty;

        // ⚠️ The LIVE stream is deliberately NOT rewritten: `done.answer == Σ delta` is a contract the client
        // relies on to recover a lost frame, and redacting frame-by-frame would both break it (an identifier
        // straddles a chunk boundary) and destroy any proposal block it touched. Filtering the live text is
        // the client's job (`redactIdentifiers.ts`), which is why that rule is part of the API contract.
        Assert.Contains(leakedId.ToString(), answer, StringComparison.OrdinalIgnoreCase);

        // What the SERVER owns is what becomes durable — and that is filtered, so a reload cannot resurrect
        // what the user was never shown.
        var stored = await client.GetJsonAsync<List<JsonElement>>(
            $"/api/boards/{seed.Board.Id}/agent-chat/messages");

        var storedAnswer = stored!
            .Single(m => m.GetProperty("role").GetString() == "assistant")
            .GetProperty("content").GetString() ?? string.Empty;

        Assert.DoesNotContain(leakedId.ToString(), storedAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(AiChatAnswerSanitizer.Placeholder, storedAnswer, StringComparison.Ordinal);
        Assert.False(AiChatAnswerSanitizer.ContainsIdentifier(storedAnswer));

        // The prose is otherwise intact — a redaction, not a deletion.
        Assert.StartsWith("Sau khi có thẻ trong ngữ cảnh", storedAnswer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PHASE146A_A6_ATaskTurnIsRedactedBeforeItIsStoredToo()
    {
        var leakedId = Guid.NewGuid();
        var scripted = new ScriptedAiProvider
        {
            StreamChunks = [$"Cột liên quan có id {leakedId}, bạn nên xử lý trước."],
        };

        await using var scenario = await _database.CreateScenarioAsync(scriptedAi: scripted);
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        await SendTaskChatAsync(client, seed.Task.Id, "Thẻ này thế nào?");

        var stored = await client.GetJsonAsync<List<JsonElement>>(
            $"/api/tasks/{seed.Task.Id}/agent-chat/messages");

        var storedAnswer = stored!
            .Single(m => m.GetProperty("role").GetString() == "assistant")
            .GetProperty("content").GetString() ?? string.Empty;

        Assert.DoesNotContain(leakedId.ToString(), storedAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.False(AiChatAnswerSanitizer.ContainsIdentifier(storedAnswer));
    }

    // =====================================================================
    // A4 — the lightweight board digest
    // =====================================================================

    [Fact]
    public async Task PHASE146A_A7_WithNothingAttachedThePromptCarriesTheBoardDigest()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        // Two more cards so "how is the board doing?" has something to summarize, one of them urgent.
        await scenario.CreateTaskAsync(seed.Board, seed.Todo, seed.User, "Chuẩn bị báo cáo quý");
        await scenario.CreateTaskAsync(seed.Board, seed.Done, seed.User, "Đã xong việc cũ");

        var prepared = await PrepareBoardChatAsync(scenario, seed, attachedTaskIds: []);

        // The digest, with the two things the question needs: how much is where, and what matters most.
        // `DigestBlockMarker` rather than the bare heading — rule 7 of the system prompt quotes the heading by
        // name, so only the block's opening line proves the digest is really there.
        Assert.Contains(AgentChatPrompts.DigestBlockMarker, prepared.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains($"* {seed.Todo.Name}: 2", prepared.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains($"* {seed.Done.Name}: 1", prepared.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Chuẩn bị báo cáo quý", prepared.SystemPrompt, StringComparison.Ordinal);

        // §A2's rule travels with the prompt. (The rendered prompt DOES contain real column/member ids by
        // design — the model needs them to propose a move or an assignee — so the redaction rule is asserted on
        // the digest's own content in AgentChatDigestTests rather than on the whole prompt here.)
        Assert.Contains("KHÔNG hiển thị raw ID, GUID hoặc UUID", prepared.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PHASE146A_A8_WithACardAttachedTheDigestIsNotAdded()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        await scenario.CreateTaskAsync(seed.Board, seed.Todo, seed.User, "Thẻ khác không được gắn");

        var prepared = await PrepareBoardChatAsync(scenario, seed, attachedTaskIds: [seed.Task.Id]);

        // The attachment list already describes what the user is asking about; board-wide statistics on top
        // would spend tokens on a question nobody asked.
        Assert.DoesNotContain(AgentChatPrompts.DigestBlockMarker, prepared.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains(seed.Task.Title, prepared.SystemPrompt, StringComparison.Ordinal);
    }

    // ---- helpers ----------------------------------------------------------

    private sealed record Seed(
        ApplicationUser User,
        Workspace Workspace,
        Board Board,
        BoardColumn Todo,
        BoardColumn Done,
        BoardTask Task);

    private static async Task<Seed> SeedAsync(TestScenario scenario)
    {
        var user = await scenario.CreateUserAsync("Chủ workspace");
        var workspace = await scenario.CreateWorkspaceAsync(user, "Workspace 14.6");
        var (board, todo, done) = await scenario.CreateBoardAsync(workspace);
        var task = await scenario.CreateTaskAsync(board, todo, user, "Thiết kế API chat");

        return new Seed(user, workspace, board, todo, done, task);
    }

    /// <summary>
    /// Composes the board chat's prompt by calling the service directly, so the digest and the prompt rules —
    /// which no SSE frame echoes back — can be asserted against the real database.
    /// </summary>
    private static async Task<AgentChatPreparedRequest> PrepareBoardChatAsync(
        TestScenario scenario,
        Seed seed,
        IReadOnlyList<Guid> attachedTaskIds)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        var chat = scope.ServiceProvider.GetRequiredService<IAgentChatService>();

        var request = new AgentChatSendRequest(
            [new AiChatMessageRequest("user", "Tiến độ bảng thế nào?")],
            attachedTaskIds.Select(id => new AgentChatAttachmentRequest(id)).ToList());

        // Giai đoạn 14.7 Track B §B1 added the optional `persona` argument; passing null keeps this suite on
        // the default persona, which is the behaviour it was written to pin.
        return await chat.PrepareAsync(
            boardId: seed.Board.Id,
            request: request,
            persona: null,
            userId: seed.User.Id);
    }

    private static async Task<List<SseFrame>> SendTaskChatAsync(
        TestHttpClient client, Guid taskId, string content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tasks/{taskId}/ai-chat/stream")
        {
            Content = JsonContent.Create(new { messages = new[] { new { role = "user", content } } }),
        };

        using var response = await client.SendStreamingAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return ParseFrames(await response.Content.ReadAsStringAsync());
    }

    private static async Task<List<SseFrame>> SendBoardChatAsync(
        TestHttpClient client, Guid boardId, Guid taskId, string content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/boards/{boardId}/agent-chat/stream")
        {
            Content = JsonContent.Create(new
            {
                messages = new[] { new { role = "user", content } },
                attachedTaskIds = new[] { new { taskId } },
            }),
        };

        using var response = await client.SendStreamingAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return ParseFrames(await response.Content.ReadAsStringAsync());
    }

    private static List<SseFrame> ParseFrames(string body)
    {
        var frames = new List<SseFrame>();
        var currentEvent = "message";
        var data = new StringBuilder();

        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    frames.Add(new SseFrame(currentEvent, JsonDocument.Parse(data.ToString()).RootElement.Clone()));
                    data.Clear();
                }

                currentEvent = "message";
                continue;
            }

            if (line.StartsWith(':'))
            {
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                currentEvent = line["event:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(line["data:".Length..].Trim());
            }
        }

        if (data.Length > 0)
        {
            frames.Add(new SseFrame(currentEvent, JsonDocument.Parse(data.ToString()).RootElement.Clone()));
        }

        return frames;
    }

    private sealed record SseFrame(string Event, JsonElement Data);
}
