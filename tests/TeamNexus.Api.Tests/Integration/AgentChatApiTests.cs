using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 14.5 §B — the board-scoped AI Agent Chat (AGENTCHAT-1…AGENTCHAT-22).
/// <para>
/// <b>Why the stream is read as a raw body:</b> the answer has to reach the client while it is still being
/// produced, so each chat test reads the response with <see cref="HttpCompletionOption.ResponseHeadersRead"/>
/// and parses the SSE frames the way the browser will. Asserting on a deserialized object would prove the
/// endpoint answers, not that it streams. (The in-process <c>TestServer</c> may still deliver one buffer
/// regardless of <c>FlushAsync</c> — the phase report states that boundary; what is asserted here is the
/// frame contract plus the strict <c>meta</c>-before-<c>delta</c> order.)
/// </para>
/// <para>
/// <b>What only this suite can prove:</b> that the transcript survives a reload, that the whole
/// stream → parse → submit → Pending → approve path really moves a card offline, and that the attachment
/// scope is enforced against the live board rather than trusted from the request.
/// </para>
/// </summary>
public sealed class AgentChatApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public AgentChatApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- session + transcript ---------------------------------------------

    [Fact]
    public async Task AGENTCHAT1_SessionIsIdempotentAndDoesNotCreateAnything()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var first = await client.GetAsync($"/api/boards/{seed.Board.Id}/agent-chat/session");
        var second = await client.GetAsync($"/api/boards/{seed.Board.Id}/agent-chat/session");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(0, await scenario.CountAsync<AgentChatMessage>());

        var payload = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(seed.Board.Id, payload.GetProperty("boardId").GetGuid());
        Assert.False(payload.GetProperty("hasMessages").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("model").GetString()));
    }

    [Fact]
    public async Task AGENTCHAT2_StreamingAnswersWithTheSharedSseContract()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var frames = await SendChatAsync(client, seed.Board.Id, seed.Task.Id, "Thẻ này nên làm gì tiếp?");

        Assert.Single(Named(frames, AiChatSseWriter.MetaEvent));
        Assert.Single(Named(frames, AiChatSseWriter.DoneEvent));
        Assert.Empty(Named(frames, AiChatSseWriter.ErrorEvent));
        Assert.True(frames.Count(f => f.Event == AiChatSseWriter.DeltaEvent) >= 1);

        // meta first, done last, and done.answer == the concatenation of every delta — the invariant the
        // client relies on to recover when a delta frame is lost.
        Assert.Equal(AiChatSseWriter.MetaEvent, frames[0].Event);
        Assert.Equal(AiChatSseWriter.DoneEvent, frames[^1].Event);

        var streamed = string.Concat(frames
            .Where(f => f.Event == AiChatSseWriter.DeltaEvent)
            .Select(f => f.Data.GetProperty("text").GetString()));

        var done = frames.Single(f => f.Event == AiChatSseWriter.DoneEvent).Data;
        Assert.Equal(streamed, done.GetProperty("answer").GetString());

        var meta = Named(frames, AiChatSseWriter.MetaEvent).Single().Data;
        Assert.Equal(seed.Board.Id, meta.GetProperty("boardId").GetGuid());
        Assert.Equal(seed.Workspace.Id, meta.GetProperty("workspaceId").GetGuid());
    }

    [Fact]
    public async Task AGENTCHAT3_TheAnswerIsStoredAndSurvivesAReload()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        await SendChatAsync(client, seed.Board.Id, seed.Task.Id, "Tóm tắt tình hình bảng");

        // One question + one answer, both persisted.
        Assert.Equal(2, await scenario.CountAsync<AgentChatMessage>());

        var response = await client.GetAsync($"/api/boards/{seed.Board.Id}/agent-chat/messages");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var messages = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.NotNull(messages);
        Assert.Equal(2, messages!.Count);

        // Oldest first, so the UI can render the conversation top-down.
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
        Assert.Equal("Tóm tắt tình hình bảng", messages[0].GetProperty("content").GetString());

        // The stored answer must NOT contain the JSON proposal block: the transcript is prose a human
        // reads, while the proposal lives in the action log.
        var storedAnswer = messages[1].GetProperty("content").GetString() ?? string.Empty;
        Assert.DoesNotContain("```json", storedAnswer);
        Assert.NotEmpty(storedAnswer);

        // The attachments of the turn are recorded so the UI can redraw its pills.
        var attachments = messages[0].GetProperty("attachments");
        Assert.Equal(JsonValueKind.Array, attachments.ValueKind);
        Assert.Equal(seed.Task.Id, attachments[0].GetProperty("taskId").GetGuid());
    }

    [Fact]
    public async Task AGENTCHAT4_TranscriptPagingUsesTheBeforeCursor()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        await SendChatAsync(client, seed.Board.Id, seed.Task.Id, "Lượt một");
        await SendChatAsync(client, seed.Board.Id, seed.Task.Id, "Lượt hai");

        var firstPage = await client.GetJsonAsync<List<JsonElement>>(
            $"/api/boards/{seed.Board.Id}/agent-chat/messages?take=2");

        Assert.NotNull(firstPage);
        Assert.Equal(2, firstPage!.Count);

        var oldestLoaded = firstPage[0].GetProperty("createdAt").GetDateTimeOffset();
        var before = Uri.EscapeDataString(oldestLoaded.ToString("O"));

        var olderPage = await client.GetJsonAsync<List<JsonElement>>(
            $"/api/boards/{seed.Board.Id}/agent-chat/messages?take=2&before={before}");

        Assert.NotNull(olderPage);
        Assert.Equal(2, olderPage!.Count);
        // Strictly older than the cursor, so the two pages cannot overlap.
        Assert.All(
            olderPage,
            m => Assert.True(m.GetProperty("createdAt").GetDateTimeOffset() < oldestLoaded));
    }

    // ---- authorization ----------------------------------------------------

    [Fact]
    public async Task AGENTCHAT5_SomeoneOutsideTheWorkspaceGets404NotA403()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        var outsider = await scenario.CreateUserAsync("Người ngoài");
        using var client = await scenario.AsUserAsync(outsider);

        var response = await PostChatAsync(client, seed.Board.Id, seed.Task.Id, "Cho tôi xem bảng này");

        // 404 (not 403): the endpoint must not confirm that this workspace exists.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AGENTCHAT6_UnauthenticatedIs401AndMissingCsrfIs403()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        using var anonymous = scenario.Factory.CreateDefaultClient();
        anonymous.BaseAddress = TestScenario.BaseAddress;

        var unauthenticated = await anonymous.PostAsJsonAsync(
            $"/api/boards/{seed.Board.Id}/agent-chat/stream",
            Text("Chưa đăng nhập"));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using var client = await scenario.AsUserAsync(seed.User);

        // A raw HttpClient deliberately omits the CSRF header (the TestHttpClient always attaches it).
        var bare = new HttpRequestMessage(
            HttpMethod.Post, $"/api/boards/{seed.Board.Id}/agent-chat/stream")
        {
            Content = JsonContent.Create(Text("Thiếu CSRF")),
        };

        var missingCsrf = await client.Http.SendAsync(bare);
        Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
    }

    // ---- attachments: the scope boundary ----------------------------------

    [Fact]
    public async Task AGENTCHAT7_MoreThanTheAttachmentCapIs400BeforeAnyProviderCall()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        // Nine tasks: one over the production cap of eight.
        var taskIds = new List<Guid> { seed.Task.Id };
        for (var i = 0; i < 8; i++)
        {
            var extra = await scenario.CreateTaskAsync(seed.Board, seed.Todo, seed.User, $"Thẻ phụ {i}");
            taskIds.Add(extra.Id);
        }

        using var client = await scenario.AsUserAsync(seed.User);

        var response = await PostChatAsync(client, seed.Board.Id, taskIds, "Quá nhiều thẻ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("tối đa 8 thẻ", await ErrorAsync(response));

        // Nothing was written and nothing was asked of the provider.
        Assert.Equal(0, await scenario.CountAsync<AgentChatMessage>());
    }

    [Fact]
    public async Task AGENTCHAT8_ATaskFromAnotherBoardIs400()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        // Second board in the same workspace: the task exists and the caller may see it, but it is not
        // part of this conversation's board.
        var (otherBoard, otherTodo, _) = await scenario.CreateBoardAsync(seed.Workspace);
        var foreignTask = await scenario.CreateTaskAsync(otherBoard, otherTodo, seed.User, "Thẻ bảng khác");

        using var client = await scenario.AsUserAsync(seed.User);

        var response = await PostChatAsync(client, seed.Board.Id, [foreignTask.Id], "Thẻ này thuộc bảng nào?");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("không thuộc bảng này", await ErrorAsync(response));
    }

    [Fact]
    public async Task AGENTCHAT9_TranscriptOverTheBoardCapIs400()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        // 25 messages against a cap of 24.
        var messages = Enumerable.Range(0, 25)
            .Select(i => new { role = i % 2 == 0 ? "user" : "assistant", content = $"Tin nhắn {i}" })
            .ToArray();

        var response = await PostChatAsync(client, seed.Board.Id, new { messages });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("24 tin nhắn", await ErrorAsync(response));
        Assert.Equal(0, await scenario.CountAsync<AgentChatMessage>());
    }

    // ---- the two human-approved write paths --------------------------------

    [Fact]
    public async Task AGENTCHAT10_SavingTheAnswerCreatesAPendingCommentOnTheAttachedTask()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var response = await client.PostJsonAsync(
            $"/api/boards/{seed.Board.Id}/agent-chat/message",
            new { content = "Kết luận: nên tách thẻ này thành hai.", taskId = seed.Task.Id });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var log = await response.Content.ReadFromJsonAsync<AiActionLogDto>();
        Assert.NotNull(log);
        Assert.Equal("PostComment", log!.Action);
        Assert.Equal("Task", log.EntityType);
        Assert.Equal(seed.Task.Id, log.EntityId);
        Assert.Equal("Pending", log.Status);

        // Pending means exactly that: the comment itself does not exist yet.
        Assert.Equal(0, await scenario.CountAsync<TaskComment>());
    }

    [Fact]
    public async Task AGENTCHAT11_SavingWithoutAnAttachedTaskIs400()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var response = await client.PostJsonAsync(
            $"/api/boards/{seed.Board.Id}/agent-chat/message",
            new { content = "Không gắn thẻ nào" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("gắn ít nhất một thẻ", await ErrorAsync(response));
    }

    [Fact]
    public async Task AGENTCHAT12_SavingMoreThanTwoThousandCharactersIs400()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var response = await client.PostJsonAsync(
            $"/api/boards/{seed.Board.Id}/agent-chat/message",
            new { content = new string('a', 2001), taskId = seed.Task.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("2000", await ErrorAsync(response));
    }

    // ---- proposals: parse → submit → Pending → approve → real move ---------

    [Fact]
    public async Task AGENTCHAT13_AProposalFromTheAnswerBecomesAPendingMoveThatReallyMoves()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        // The offline provider answers with a real proposal built from the ids in the context block.
        var frames = await SendChatAsync(client, seed.Board.Id, seed.Task.Id, "Nên chuyển thẻ này đi đâu?");
        var answer = frames.Single(f => f.Event == AiChatSseWriter.DoneEvent).Data
            .GetProperty("answer").GetString();

        var parsed = AgentChatProposalParser.Parse(answer);
        Assert.NotNull(parsed.JsonBlock);

        var proposal = JsonDocument.Parse(parsed.JsonBlock!).RootElement;
        var targetColumnId = proposal.GetProperty("payload").GetProperty("columnId").GetGuid();

        var confirm = await client.PostJsonAsync(
            $"/api/boards/{seed.Board.Id}/agent-chat/proposal/confirm",
            new
            {
                proposalJson = parsed.JsonBlock,
                attachedTaskIds = new[] { new { taskId = seed.Task.Id } },
            });

        Assert.Equal(HttpStatusCode.Created, confirm.StatusCode);

        var log = await confirm.Content.ReadFromJsonAsync<AiActionLogDto>();
        Assert.NotNull(log);
        Assert.Equal("MoveTaskToColumn", log!.Action);
        Assert.Equal("Pending", log.Status);

        // Still Pending ⇒ nothing moved yet. This is the invariant the whole phase rests on.
        var beforeApprove = await scenario.FindAsync<BoardTask>(seed.Task.Id);
        Assert.Equal(seed.Todo.Id, beforeApprove!.ColumnId);

        // A Manager approves through the pre-existing Phase 4 endpoint.
        using var managerClient = await scenario.AsUserAsync(seed.User, "Manager");
        var approve = await managerClient.PostJsonAsync($"/api/ai-actions/{log.Id}/approve", new { });
        Assert.True(
            approve.StatusCode == HttpStatusCode.OK,
            $"approve failed: {approve.StatusCode} body={await BodyAsync(approve)}");

        var afterApprove = await scenario.FindAsync<BoardTask>(seed.Task.Id);
        Assert.Equal(targetColumnId, afterApprove!.ColumnId);

        // Undo returns the card to the column it came from — the value recorded at apply time.
        var undo = await managerClient.PostJsonAsync($"/api/ai-actions/{log.Id}/undo", new { });
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);

        var afterUndo = await scenario.FindAsync<BoardTask>(seed.Task.Id);
        Assert.Equal(seed.Todo.Id, afterUndo!.ColumnId);
    }

    [Fact]
    public async Task AGENTCHAT14_AProposalNamingATaskThatWasNotAttachedIs400()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var second = await scenario.CreateTaskAsync(seed.Board, seed.Todo, seed.User, "Thẻ không được gắn");

        var proposalJson = JsonSerializer.Serialize(new
        {
            kind = "MoveTaskToColumn",
            summary = "Chuyển thẻ",
            payload = new { taskId = second.Id, columnId = seed.Done.Id },
        });

        // The attachment scope is re-derived on the server; naming a task in the body is not enough.
        var response = await client.PostJsonAsync(
            $"/api/boards/{seed.Board.Id}/agent-chat/proposal/confirm",
            new
            {
                proposalJson,
                attachedTaskIds = new[] { new { taskId = seed.Task.Id } },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("không được gắn", await ErrorAsync(response));
        Assert.Equal(0, await scenario.CountAsync<AiActionLog>());
    }

    [Fact]
    public async Task AGENTCHAT15_AProposalForAColumnOfAnotherBoardIs400()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var (_, otherTodo, _) = await scenario.CreateBoardAsync(seed.Workspace);

        var proposalJson = JsonSerializer.Serialize(new
        {
            kind = "MoveTaskToColumn",
            summary = "Chuyển sang cột của bảng khác",
            payload = new { taskId = seed.Task.Id, columnId = otherTodo.Id },
        });

        var response = await client.PostJsonAsync(
            $"/api/boards/{seed.Board.Id}/agent-chat/proposal/confirm",
            new
            {
                proposalJson,
                attachedTaskIds = new[] { new { taskId = seed.Task.Id } },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("không thuộc bảng này", await ErrorAsync(response));
    }

    [Fact]
    public async Task AGENTCHAT16_AnUpdateProposalWritesTheFieldsAndUndoRestoresThem()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var dueDate = DateTimeOffset.Parse("2026-05-01T09:00:00+07:00");

        var proposalJson = JsonSerializer.Serialize(new
        {
            kind = "UpdateTaskFields",
            summary = "Đổi ưu tiên và hạn chót",
            payload = new
            {
                taskId = seed.Task.Id,
                title = "Tiêu đề mới",
                priority = "Urgent",
                // A +07:00 offset on purpose: the BUG-2 trap must be normalized to UTC before it reaches
                // Npgsql, otherwise the approve call would be a 500 instead of a real update.
                dueDate,
            },
        });

        var confirm = await client.PostJsonAsync(
            $"/api/boards/{seed.Board.Id}/agent-chat/proposal/confirm",
            new
            {
                proposalJson,
                attachedTaskIds = new[] { new { taskId = seed.Task.Id } },
            });

        Assert.Equal(HttpStatusCode.Created, confirm.StatusCode);
        var log = await confirm.Content.ReadFromJsonAsync<AiActionLogDto>();
        Assert.NotNull(log);
        Assert.Equal("UpdateTaskFields", log!.Action);

        using var managerClient = await scenario.AsUserAsync(seed.User, "Manager");
        var approve = await managerClient.PostJsonAsync($"/api/ai-actions/{log.Id}/approve", new { });
        Assert.True(
            approve.StatusCode == HttpStatusCode.OK,
            $"approve failed: {approve.StatusCode} body={await BodyAsync(approve)}");

        var updated = await scenario.FindAsync<BoardTask>(seed.Task.Id);
        Assert.Equal("Tiêu đề mới", updated!.Title);
        Assert.Equal(TaskPriority.Urgent, updated.Priority);
        Assert.NotNull(updated.DueDate);
        // Stored as UTC: the offset is gone, the instant is preserved.
        Assert.Equal(TimeSpan.Zero, updated.DueDate!.Value.Offset);
        Assert.Equal(dueDate.UtcDateTime, updated.DueDate.Value.UtcDateTime);

        var undo = await managerClient.PostJsonAsync($"/api/ai-actions/{log.Id}/undo", new { });
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);

        var restored = await scenario.FindAsync<BoardTask>(seed.Task.Id);
        Assert.Equal(seed.Task.Title, restored!.Title);
        Assert.Null(restored.DueDate);
    }

    [Fact]
    public async Task AGENTCHAT17_CreateSubtasksProposalUsesTheExistingApplier()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var proposalJson = JsonSerializer.Serialize(new
        {
            kind = "CreateSubtasks",
            summary = "Chia nhỏ công việc",
            payload = new
            {
                columnId = seed.Todo.Id,
                tasks = new object[]
                {
                    new { title = "Viết API", priority = "High" },
                    new { title = "Viết test" },
                },
            },
        });

        var confirm = await client.PostJsonAsync(
            $"/api/boards/{seed.Board.Id}/agent-chat/proposal/confirm",
            new
            {
                proposalJson,
                attachedTaskIds = new[] { new { taskId = seed.Task.Id } },
            });

        Assert.Equal(HttpStatusCode.Created, confirm.StatusCode);
        var log = await confirm.Content.ReadFromJsonAsync<AiActionLogDto>();
        Assert.Equal("CreateSubtasks", log!.Action);
        Assert.Equal("Board", log.EntityType);

        // Confirm only creates the log — the Phase 4 invariant, re-proven for the chat entry point.
        Assert.Equal(1, await scenario.CountAsync<BoardTask>());

        using var managerClient = await scenario.AsUserAsync(seed.User, "Manager");
        var approve = await managerClient.PostJsonAsync($"/api/ai-actions/{log.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        Assert.Equal(3, await scenario.CountAsync<BoardTask>());
    }

    [Fact]
    public async Task AGENTCHAT18_MalformedProposalJsonIs400WithAVietnameseReason()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var response = await client.PostJsonAsync(
            $"/api/boards/{seed.Board.Id}/agent-chat/proposal/confirm",
            new
            {
                proposalJson = "{not valid json",
                attachedTaskIds = new[] { new { taskId = seed.Task.Id } },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("JSON", await ErrorAsync(response));
    }

    [Fact]
    public async Task AGENTCHAT19_ASecondApproveIs409()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var proposalJson = JsonSerializer.Serialize(new
        {
            kind = "MoveTaskToColumn",
            summary = "Chuyển thẻ",
            payload = new { taskId = seed.Task.Id, columnId = seed.Done.Id },
        });

        var confirm = await client.PostJsonAsync(
            $"/api/boards/{seed.Board.Id}/agent-chat/proposal/confirm",
            new
            {
                proposalJson,
                attachedTaskIds = new[] { new { taskId = seed.Task.Id } },
            });

        var log = await confirm.Content.ReadFromJsonAsync<AiActionLogDto>();

        using var managerClient = await scenario.AsUserAsync(seed.User, "Manager");
        var first = await managerClient.PostJsonAsync($"/api/ai-actions/{log!.Id}/approve", new { });
        var second = await managerClient.PostJsonAsync($"/api/ai-actions/{log.Id}/approve", new { });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task AGENTCHAT20_AnswerAndProposalEndpointsNeedMembership()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        var outsider = await scenario.CreateUserAsync("Người ngoài");
        using var client = await scenario.AsUserAsync(outsider);

        var save = await client.PostJsonAsync(
            $"/api/boards/{seed.Board.Id}/agent-chat/message",
            new { content = "Thử lưu", taskId = seed.Task.Id });

        var propose = await client.PostJsonAsync(
            $"/api/boards/{seed.Board.Id}/agent-chat/proposal/confirm",
            new
            {
                proposalJson = "{}",
                attachedTaskIds = new[] { new { taskId = seed.Task.Id } },
            });

        Assert.Equal(HttpStatusCode.NotFound, save.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, propose.StatusCode);
        Assert.Equal(0, await scenario.CountAsync<AiActionLog>());
    }

    [Fact]
    public async Task AGENTCHAT21_AnUnknownBoardIs404()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var response = await PostChatAsync(client, Guid.NewGuid(), seed.Task.Id, "Bảng không tồn tại");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AGENTCHAT22_TheTaskChatMetaIsUnchangedAndItsTranscriptStaysTaskScoped()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        // Phase 14's CONTRACT must survive Phase 14.5/14.6 untouched: the two chats share a transport, not a
        // feature, and a shared refactor that changed the task chat's SSE shape would be a silent regression.
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/tasks/{seed.Task.Id}/ai-chat/stream")
        {
            Content = JsonContent.Create(new
            {
                messages = new[] { new { role = "user", content = "Task này thế nào?" } },
            }),
        };

        using var response = await client.SendStreamingAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var frames = ParseFrames(await response.Content.ReadAsStringAsync());
        var meta = Named(frames, AiChatSseWriter.MetaEvent).Single().Data;

        // The task chat's meta still names the TASK — the board chat did not take that over.
        Assert.Equal(seed.Task.Id, meta.GetProperty("taskId").GetGuid());
        Assert.Equal(seed.Task.Title, meta.GetProperty("taskTitle").GetString());

        // ⚠️ Phase 14.6 §A3 CHANGES this half of Phase 14's D2 on purpose, and this is the ONLY assertion of
        // Phase 14.5 that had to move: "the task chat keeps no transcript at all (D2)" becomes "the task chat
        // keeps its own transcript, under task_id". The whole point of A3 is that a conversation at the task
        // tab used to vanish on F5; leaving it unrecorded is exactly the defect being fixed.
        //
        // What must NOT change — and is what this test now guards — is the SCOPE: task turns carry a
        // `task_id` and no `board_id`, so asking a question in the task tab can never appear in the board's
        // AI Agent Chat (and vice versa). The CHECK constraint from Migration 11 makes that structural.
        Assert.Equal(0, await scenario.CountAsync<AgentChatMessage>(m => m.BoardId != null));
        Assert.Equal(2, await scenario.CountAsync<AgentChatMessage>(m => m.TaskId == seed.Task.Id));

        var stored = await client.GetJsonAsync<List<JsonElement>>(
            $"/api/tasks/{seed.Task.Id}/agent-chat/messages");

        Assert.NotNull(stored);
        Assert.Equal(2, stored!.Count);

        // ...and it stays invisible to the board conversation, which is the isolation the assertion above
        // describes in the database rather than in an HTTP response.
        var boardTranscript = await client.GetJsonAsync<List<JsonElement>>(
            $"/api/boards/{seed.Board.Id}/agent-chat/messages");

        Assert.NotNull(boardTranscript);
        Assert.Empty(boardTranscript!);
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
        var workspace = await scenario.CreateWorkspaceAsync(user, "Workspace Agent Chat");
        var (board, todo, done) = await scenario.CreateBoardAsync(workspace);
        var task = await scenario.CreateTaskAsync(board, todo, user, "Chuẩn bị báo cáo quý");

        return new Seed(user, workspace, board, todo, done, task);
    }

    private static object Text(string content) => new { messages = new[] { new { role = "user", content } } };

    private static Task<HttpResponseMessage> PostChatAsync(
        TestHttpClient client, Guid boardId, Guid taskId, string content)
        => PostChatAsync(client, boardId, [taskId], content);

    private static async Task<HttpResponseMessage> PostChatAsync(
        TestHttpClient client, Guid boardId, IEnumerable<Guid> taskIds, string content)
    {
        var body = new
        {
            messages = new[] { new { role = "user", content } },
            attachedTaskIds = taskIds.Select(id => new { taskId = id }).ToArray(),
        };

        return await PostChatAsync(client, boardId, body);
    }

    private static async Task<HttpResponseMessage> PostChatAsync(
        TestHttpClient client, Guid boardId, object body)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/boards/{boardId}/agent-chat/stream")
        {
            Content = JsonContent.Create(body),
        };

        return await client.SendStreamingAsync(request);
    }

    private static async Task<List<SseFrame>> SendChatAsync(
        TestHttpClient client, Guid boardId, Guid taskId, string content)
    {
        using var response = await PostChatAsync(client, boardId, taskId, content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        return ParseFrames(await response.Content.ReadAsStringAsync());
    }

    private static IEnumerable<SseFrame> Named(IEnumerable<SseFrame> frames, string eventName)
        => frames.Where(f => string.Equals(f.Event, eventName, StringComparison.Ordinal));

    private static async Task<string> ErrorAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.TryGetProperty("error", out var error) ? error.GetString() ?? string.Empty : string.Empty;
    }

    /// <summary>Full body of a response, for diagnosing a 500 that has no <c>{ error }</c> shape.</summary>
    private static async Task<string> BodyAsync(HttpResponseMessage response)
        => await response.Content.ReadAsStringAsync();

    /// <summary>
    /// Minimal SSE reader, deliberately a local copy: the browser's parser is what the contract is written
    /// against, and duplicating ~15 lines here keeps a server bug from being masked by a helper bug.
    /// </summary>
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

    private sealed record AiActionLogDto(
        Guid Id,
        string Action,
        string EntityType,
        Guid? EntityId,
        string Status);
}
