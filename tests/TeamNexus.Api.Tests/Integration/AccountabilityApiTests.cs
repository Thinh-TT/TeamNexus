using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 8 §2.5 — AI Smart Setup and the Accountability Layer end-to-end, fully offline.
/// <para>
/// The invariant under test is the product's core promise: <b>AI never writes business data
/// directly</b>. A proposal is generated → a <c>Pending</c> log is created → only an explicit human
/// approval writes tasks → rejection writes nothing → undo reverts what was written.
/// </para>
/// </summary>
public sealed class AccountabilityApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public AccountabilityApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- generation writes nothing -----------------------------------------

    [Fact]
    public async Task SmartSetup_GeneratesAProposalWithoutWritingAnything()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, board, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var before = await scenario.CountAsync<BoardTask>();

        var response = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/smart-setup",
            new { description = "Xây tính năng báo cáo tuần cho nhóm" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var proposal = await response.Content.ReadFromJsonAsync<SmartSetupProposal>();
        Assert.NotNull(proposal);
        Assert.NotEmpty(proposal!.Tasks);

        // The whole point of Phase 3: a proposal is a suggestion, never a write.
        Assert.Equal(before, await scenario.CountAsync<BoardTask>());
        Assert.Equal(0, await scenario.CountAsync<AiActionLog>());
    }

    [Fact]
    public async Task SmartSetup_ResolvesASuggestedAssigneeAgainstTheWorkspaceMembers()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, board, _) = await SeedAsync(scenario, withLinh: true);
        using var client = await scenario.AsUserAsync(user);

        var response = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/smart-setup",
            new { description = "Phân rã công việc cho tuần tới" });

        var proposal = await response.Content.ReadFromJsonAsync<SmartSetupProposal>();
        Assert.NotNull(proposal);

        // The offline fake proposes "Linh" on its first task; that member exists in this workspace,
        // so the proposal must mark the suggestion as matched and carry a real user id.
        var suggestion = proposal!.Tasks.Select(t => t.Assignee).FirstOrDefault(a => a is not null);

        Assert.NotNull(suggestion);
        Assert.True(suggestion!.Matched);
        Assert.NotNull(suggestion.UserId);
    }

    [Fact]
    public async Task SmartSetup_WithUnparsableAiOutput_Returns502AndWritesNothing()
    {
        var scripted = new ScriptedAiProvider { Content = "không phải JSON" };

        await using var scenario = await _database.CreateScriptedAiScenarioAsync(scripted);
        var (user, board, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var response = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/smart-setup",
            new { description = "Mô tả bất kỳ" });

        // A provider that keeps answering with garbage is an upstream failure → 502, NOT a client
        // error: the request itself was valid. (AiProviderException ⇒ 502 by contract, Phase 3 §2.1.)
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        // The service retries once with a stricter prompt, then gives up — it must not invent data.
        Assert.Equal(2, scripted.CallCount);
        Assert.Equal(0, await scenario.CountAsync<BoardTask>());
        Assert.Equal(0, await scenario.CountAsync<AiActionLog>());
    }

    // ---- confirm → Pending --------------------------------------------------

    [Fact]
    public async Task Confirm_CreatesAPendingLogWithoutCreatingTasks()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, board, todo) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var response = await ConfirmAsync(client, scenario, board.Id, todo.Id, "Task A", "Task B");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var log = await response.Content.ReadFromJsonAsync<AiActionLogResponse>();
        Assert.NotNull(log);
        Assert.Equal("Pending", log!.Status);
        Assert.Equal("CreateSubtasks", log.Action);
        Assert.Equal("Board", log.EntityType);
        Assert.Equal(2, log.TaskCount);
        Assert.Equal(user.Id, log.RequestedByUserId);

        // Pending means "recorded", not "applied".
        Assert.Equal(0, await scenario.CountAsync<BoardTask>());
    }

    [Fact]
    public async Task Confirm_WithNoTasks_IsRejectedWith400()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, board, todo) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var response = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/smart-setup/confirm",
            new { description = "Không có task nào", summary = (string?)null, tasks = Array.Empty<object>(), columnId = todo.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await scenario.CountAsync<AiActionLog>());
    }

    // ---- approve ------------------------------------------------------------

    [Fact]
    public async Task Approve_WritesTheTasksAndMarksTheLogApproved()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, board, todo) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var log = await ConfirmLogAsync(client, scenario, board.Id, todo.Id, "Task A", "Task B");

        var approve = await client.PostJsonAsync($"/api/ai-actions/{log.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var detail = await approve.Content.ReadFromJsonAsync<AiActionLogDetailResponse>();
        Assert.Equal("Approved", detail!.Status);
        Assert.Equal(2, detail.CreatedTaskIds.Count);
        Assert.Equal(user.Id, detail.DecidedByUserId);
        Assert.NotNull(detail.DecidedAt);

        // The tasks exist, in the requested column, owned by the proposer.
        var tasks = await client.GetJsonAsync<List<TaskResponse>>($"/api/boards/{board.Id}/tasks");
        Assert.NotNull(tasks);
        Assert.Equal(["Task A", "Task B"], tasks!.Select(t => t.Title).OrderBy(t => t));
        Assert.All(tasks, t => Assert.Equal(todo.Id, t.ColumnId));
    }

    [Fact]
    public async Task Approve_Twice_Returns409AndDoesNotDuplicateTasks()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, board, todo) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var log = await ConfirmLogAsync(client, scenario, board.Id, todo.Id, "Task A", "Task B");

        var first = await client.PostJsonAsync($"/api/ai-actions/{log.Id}/approve", new { });
        var second = await client.PostJsonAsync($"/api/ai-actions/{log.Id}/approve", new { });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // CAS guard: the compare-and-swap on status is what makes double-approval a no-op.
        Assert.Equal(2, await scenario.CountAsync<BoardTask>());
    }

    // ---- reject -------------------------------------------------------------

    [Fact]
    public async Task Reject_LeavesNoTaskBehindAndRecordsTheDecision()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, board, todo) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var log = await ConfirmLogAsync(client, scenario, board.Id, todo.Id, "Task A");

        var reject = await client.PostJsonAsync(
            $"/api/ai-actions/{log.Id}/reject",
            new { note = "Chưa đúng phạm vi" });

        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        var detail = await reject.Content.ReadFromJsonAsync<AiActionLogDetailResponse>();
        Assert.Equal("Rejected", detail!.Status);
        Assert.Equal("Chưa đúng phạm vi", detail.DecisionNote);
        Assert.Empty(detail.CreatedTaskIds);

        Assert.Equal(0, await scenario.CountAsync<BoardTask>());
    }

    [Fact]
    public async Task Reject_AfterApproval_Returns409()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, board, todo) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var log = await ConfirmLogAsync(client, scenario, board.Id, todo.Id, "Task A");

        await client.PostJsonAsync($"/api/ai-actions/{log.Id}/approve", new { });
        var reject = await client.PostJsonAsync($"/api/ai-actions/{log.Id}/reject", new { note = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);
    }

    // ---- undo ---------------------------------------------------------------

    [Fact]
    public async Task Undo_SoftDeletesTheCreatedTasksAndMarksTheLogUndone()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, board, todo) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var log = await ConfirmLogAsync(client, scenario, board.Id, todo.Id, "Task A", "Task B");

        var approve = await client.PostJsonAsync($"/api/ai-actions/{log.Id}/approve", new { });
        var detail = await approve.Content.ReadFromJsonAsync<AiActionLogDetailResponse>();
        var createdIds = detail!.CreatedTaskIds.ToList();

        var undo = await client.PostJsonAsync($"/api/ai-actions/{log.Id}/undo", new { });
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);

        var afterUndo = await undo.Content.ReadFromJsonAsync<AiActionLogDetailResponse>();
        Assert.Equal("Undone", afterUndo!.Status);

        // Gone from every normal read…
        var tasks = await client.GetJsonAsync<List<TaskResponse>>($"/api/boards/{board.Id}/tasks");
        Assert.NotNull(tasks);
        Assert.Empty(tasks!);

        // …but soft-deleted, so the audit trail (and reporting history) keeps the rows.
        foreach (var id in createdIds)
        {
            var row = await scenario.FindIncludingSoftDeletedAsync<BoardTask>(id);
            Assert.NotNull(row);
            Assert.NotNull(row!.DeletedAt);
        }
    }

    [Fact]
    public async Task Undo_OnAPendingAction_Returns409()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, board, todo) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var log = await ConfirmLogAsync(client, scenario, board.Id, todo.Id, "Task A");

        var undo = await client.PostJsonAsync($"/api/ai-actions/{log.Id}/undo", new { });

        // Only an Approved action can be undone; a Pending one was never applied.
        Assert.Equal(HttpStatusCode.Conflict, undo.StatusCode);
    }

    [Fact]
    public async Task Undo_Twice_Returns409()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, board, todo) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var log = await ConfirmLogAsync(client, scenario, board.Id, todo.Id, "Task A");
        await client.PostJsonAsync($"/api/ai-actions/{log.Id}/approve", new { });

        var first = await client.PostJsonAsync($"/api/ai-actions/{log.Id}/undo", new { });
        var second = await client.PostJsonAsync($"/api/ai-actions/{log.Id}/undo", new { });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // ---- history & access control -------------------------------------------

    [Fact]
    public async Task History_ListsTheBoardsActionsNewestFirst()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, board, todo) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var first = await ConfirmLogAsync(client, scenario, board.Id, todo.Id, "Task A");
        var second = await ConfirmLogAsync(client, scenario, board.Id, todo.Id, "Task B");
        await client.PostJsonAsync($"/api/ai-actions/{second.Id}/approve", new { });

        var history = await client.GetJsonAsync<List<AiActionLogResponse>>($"/api/boards/{board.Id}/ai-actions");

        Assert.NotNull(history);
        Assert.Equal(2, history!.Count);
        Assert.Equal(first.Id, history.Single(h => h.Status == "Pending").Id);
        Assert.Equal(second.Id, history.Single(h => h.Status == "Approved").Id);
    }

    [Fact]
    public async Task AiActions_ForANonMember_AreInvisible()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, board, todo) = await SeedAsync(scenario);
        using var ownerClient = await scenario.AsUserAsync(user);

        var log = await ConfirmLogAsync(ownerClient, scenario, board.Id, todo.Id, "Task A");

        var outsider = await scenario.CreateUserAsync("Người ngoài AI");
        using var outsiderClient = await scenario.AsUserAsync(outsider);

        var detail = await outsiderClient.GetAsync($"/api/ai-actions/{log.Id}");
        var approve = await outsiderClient.PostJsonAsync($"/api/ai-actions/{log.Id}/approve", new { });
        var history = await outsiderClient.GetAsync($"/api/boards/{board.Id}/ai-actions");

        // 404 rather than 403: a non-member must not even learn that this action exists.
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, approve.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);

        // …and nothing was written by the attempts.
        Assert.Equal(0, await scenario.CountAsync<BoardTask>());
    }

    [Fact]
    public async Task AiActions_ForAnUnknownId_Returns404()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var response = await client.GetAsync($"/api/ai-actions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- helpers ------------------------------------------------------------

    private static async Task<(ApplicationUser User, Board Board, BoardColumn Todo)> SeedAsync(
        TestScenario scenario, bool withLinh = false)
    {
        var user = await scenario.CreateUserAsync("Trưởng nhóm");

        // The offline fake suggests "Linh" on its first task, so a workspace member with that
        // display name is what exercises the "matched assignee" branch.
        var workspace = withLinh
            ? await scenario.CreateWorkspaceAsync(
                user,
                "Workspace AI",
                (await scenario.CreateUserAsync("Linh"), WorkspaceRole.Member))
            : await scenario.CreateWorkspaceAsync(user, "Workspace AI");

        var (board, todo, _) = await scenario.CreateBoardAsync(workspace);
        return (user, board, todo);
    }

    private static async Task<HttpResponseMessage> ConfirmAsync(
        TestHttpClient client,
        TestScenario scenario,
        Guid boardId,
        Guid columnId,
        params string[] titles)
    {
        _ = scenario;

        var tasks = titles.Select(title => new
        {
            title,
            description = (string?)null,
            priority = (string?)null,
            labels = Array.Empty<object>(),
            assignee = (object?)null,
        }).ToArray();

        return await client.PostJsonAsync(
            $"/api/boards/{boardId}/smart-setup/confirm",
            new { description = "Mô tả gốc của trưởng nhóm", summary = "Tóm tắt", tasks, columnId });
    }

    private static async Task<AiActionLogResponse> ConfirmLogAsync(
        TestHttpClient client,
        TestScenario scenario,
        Guid boardId,
        Guid columnId,
        params string[] titles)
    {
        var response = await ConfirmAsync(client, scenario, boardId, columnId, titles);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"/confirm failed ({(int)response.StatusCode}): {body}");
        }

        return (await response.Content.ReadFromJsonAsync<AiActionLogResponse>())!;
    }

    // ---- response shapes mirrored from the module DTOs ----------------------

    private sealed record SmartSetupAssignee(Guid? UserId, string? DisplayName, bool Matched);

    private sealed record SmartSetupProposalTask(
        string Title,
        string? Description,
        string? Priority,
        IReadOnlyList<JsonElement> Labels,
        SmartSetupAssignee? Assignee);

    private sealed record SmartSetupProposal(string? Summary, IReadOnlyList<SmartSetupProposalTask> Tasks);

    private sealed record TaskResponse(Guid Id, Guid ColumnId, string Title, string? Priority);
}
