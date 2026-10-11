using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 14.5 §Track A (A1.1) — the workspace-wide AI action history (WSACTION-1…WSACTION-9).
/// <para>
/// <b>The defect this suite exists to prevent:</b> <c>ai_action_logs</c> has no <c>workspace_id</c> — a
/// board-scoped action stores <c>entity_type='Board'</c> + boardId, and the board template stores
/// <c>'Workspace'</c> + workspaceId. So the board-scoped history endpoint can <b>never</b> list a log that
/// creates a board (there was no board to scope it to), and before this endpoint the only way to approve or
/// undo a board-template proposal was from inside the board it had already created.
/// </para>
/// </summary>
public sealed class WorkspaceAiActionApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public WorkspaceAiActionApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task WSACTION1_ListsBoardScopedAndWorkspaceScopedActionsTogether()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        // One board-scoped action (CreateSubtasks) …
        using var managerClient = await scenario.AsUserAsync(seed.User, "Manager");
        var subtasks = await managerClient.PostJsonAsync(
            $"/api/boards/{seed.Board.Id}/smart-setup/confirm",
            new
            {
                description = "Chia việc",
                summary = "Chia việc cho nhóm",
                columnId = seed.Todo.Id,
                tasks = new object[] { new { title = "Việc A" } },
            });

        Assert.Equal(HttpStatusCode.Created, subtasks.StatusCode);

        // … and one workspace-scoped action (the board template), which the board list cannot show.
        var template = await managerClient.PostJsonAsync(
            $"/api/workspaces/{seed.Workspace.Id}/smart-setup/template/confirm",
            new
            {
                proposal = new
                {
                    summary = "Bảng mẫu",
                    boardName = "Bảng mới",
                    boardDescription = (string?)null,
                    columns = new object[]
                    {
                        new { name = "Cần làm", isDone = false },
                        new { name = "Xong", isDone = true },
                    },
                    tasks = new object[]
                    {
                        new { title = "Thẻ 1", columnName = "Cần làm", priority = "High", labels = Array.Empty<object>(), assignee = (object?)null },
                        new { title = "Thẻ 2", columnName = "Cần làm", priority = (string?)null, labels = Array.Empty<object>(), assignee = (object?)null },
                        new { title = "Thẻ 3", columnName = "Cần làm", priority = (string?)null, labels = Array.Empty<object>(), assignee = (object?)null },
                        new { title = "Thẻ 4", columnName = "Cần làm", priority = (string?)null, labels = Array.Empty<object>(), assignee = (object?)null },
                        new { title = "Thẻ 5", columnName = "Cần làm", priority = (string?)null, labels = Array.Empty<object>(), assignee = (object?)null },
                    },
                },
            });

        Assert.Equal(HttpStatusCode.Created, template.StatusCode);

        var response = await managerClient.GetAsync($"/api/workspaces/{seed.Workspace.Id}/ai-actions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var logs = await response.Content.ReadFromJsonAsync<List<AiActionLogDto>>();
        Assert.NotNull(logs);

        // Both shapes are present — that is the whole point of this endpoint.
        Assert.Contains(logs!, l => l.Action == AiActionTypes.CreateSubtasks && l.EntityType == AiEntityTypes.Board);
        Assert.Contains(
            logs!,
            l => l.Action == AiActionTypes.CreateBoardFromTemplate && l.EntityType == AiEntityTypes.Workspace);

        // Newest first.
        Assert.True(logs![0].CreatedAt >= logs[^1].CreatedAt);
    }

    [Fact]
    public async Task WSACTION2_AMemberIsRejectedWith403()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        var member = await scenario.CreateUserAsync("Thành viên");
        await scenario.WithDbContextAsync(async db =>
        {
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = seed.Workspace.Id,
                UserId = member.Id,
                Role = WorkspaceRole.Member,
                MemberType = MemberType.Human,
                JoinedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync();
        });

        using var client = await scenario.AsUserAsync(member);

        var response = await client.GetAsync($"/api/workspaces/{seed.Workspace.Id}/ai-actions");

        // Manager+ only: the history exposes who asked for what across every board.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WSACTION3_AnOutsiderGets404()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        var outsider = await scenario.CreateUserAsync("Người ngoài");
        using var client = await scenario.AsUserAsync(outsider);

        var response = await client.GetAsync($"/api/workspaces/{seed.Workspace.Id}/ai-actions");

        // 404 rather than 403: the endpoint must not confirm that the workspace exists.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WSACTION4_AnotherWorkspaceActionsAreNotVisible()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        // A second, fully independent workspace owned by the same user.
        var otherOwner = await scenario.CreateUserAsync("Chủ khác");
        var otherWorkspace = await scenario.CreateWorkspaceAsync(otherOwner, "Workspace khác");
        var (otherBoard, otherTodo, _) = await scenario.CreateBoardAsync(otherWorkspace);

        using var otherClient = await scenario.AsUserAsync(otherOwner, "Manager");
        var created = await otherClient.PostJsonAsync(
            $"/api/boards/{otherBoard.Id}/smart-setup/confirm",
            new
            {
                description = "Việc của workspace khác",
                summary = "x",
                columnId = otherTodo.Id,
                tasks = new object[] { new { title = "Không được thấy" } },
            });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var client = await scenario.AsUserAsync(seed.User, "Manager");
        var response = await client.GetAsync($"/api/workspaces/{seed.Workspace.Id}/ai-actions");

        var logs = await response.Content.ReadFromJsonAsync<List<AiActionLogDto>>();
        Assert.NotNull(logs);

        // Nothing from the other workspace leaked in.
        Assert.DoesNotContain(logs!, l => l.EntityId == otherBoard.Id);
        Assert.Empty(logs!);
    }

    [Fact]
    public async Task WSACTION5_StatusFilterUsesTheStrictNameOnlyParse()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User, "Manager");

        var pending = await client.GetAsync($"/api/workspaces/{seed.Workspace.Id}/ai-actions?status=pending");
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);

        // A numeric enum value must be a 400, not a silent match (the same rule as the board-scoped list).
        var numeric = await client.GetAsync($"/api/workspaces/{seed.Workspace.Id}/ai-actions?status=2");
        Assert.Equal(HttpStatusCode.BadRequest, numeric.StatusCode);
    }

    [Fact]
    public async Task WSACTION6_UnknownActionAndEntityTypeFiltersReturnAnEmptyPage()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User, "Manager");

        var response = await client.GetAsync(
            $"/api/workspaces/{seed.Workspace.Id}/ai-actions?entityType=Nope&action=Nothing");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var logs = await response.Content.ReadFromJsonAsync<List<AiActionLogDto>>();
        Assert.NotNull(logs);
        Assert.Empty(logs!);
    }

    [Fact]
    public async Task WSACTION7_TheTakeParameterIsClampedRatherThanTrusted()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User, "Manager");

        // A hostile `take` must not turn into an unbounded query; the service clamps to 1..100.
        var tooBig = await client.GetAsync($"/api/workspaces/{seed.Workspace.Id}/ai-actions?take=100000");
        Assert.Equal(HttpStatusCode.OK, tooBig.StatusCode);

        var negative = await client.GetAsync($"/api/workspaces/{seed.Workspace.Id}/ai-actions?take=-5");
        Assert.Equal(HttpStatusCode.OK, negative.StatusCode);
    }

    [Fact]
    public async Task WSACTION8_ApprovingABoardTemplateWorksWithoutOpeningTheNewBoard()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User, "Manager");

        var template = await client.PostJsonAsync(
            $"/api/workspaces/{seed.Workspace.Id}/smart-setup/template/confirm",
            new
            {
                proposal = new
                {
                    summary = "Bảng mẫu",
                    boardName = "Bảng từ workspace",
                    boardDescription = (string?)null,
                    columns = new object[]
                    {
                        new { name = "Cần làm", isDone = false },
                        new { name = "Xong", isDone = true },
                    },
                    tasks = new object[]
                    {
                        new { title = "Thẻ 1", columnName = "Cần làm", priority = "High", labels = Array.Empty<object>(), assignee = (object?)null },
                        new { title = "Thẻ 2", columnName = "Cần làm", priority = (string?)null, labels = Array.Empty<object>(), assignee = (object?)null },
                        new { title = "Thẻ 3", columnName = "Cần làm", priority = (string?)null, labels = Array.Empty<object>(), assignee = (object?)null },
                        new { title = "Thẻ 4", columnName = "Cần làm", priority = (string?)null, labels = Array.Empty<object>(), assignee = (object?)null },
                        new { title = "Thẻ 5", columnName = "Cần làm", priority = (string?)null, labels = Array.Empty<object>(), assignee = (object?)null },
                    },
                },
            });

        var log = await template.Content.ReadFromJsonAsync<AiActionLogDto>();
        Assert.NotNull(log);

        var approve = await client.PostJsonAsync($"/api/ai-actions/{log!.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        // The board really exists, and its creation is visible in the workspace history.
        Assert.Equal(1, await scenario.CountAsync<Board>(b => b.Name == "Bảng từ workspace"));

        var history = await client.GetJsonAsync<List<AiActionLogDto>>(
            $"/api/workspaces/{seed.Workspace.Id}/ai-actions");

        Assert.NotNull(history);
        var entry = Assert.Single(history!, l => l.Id == log.Id);
        Assert.Equal("Approved", entry.Status);
    }

    [Fact]
    public async Task WSACTION9_TheResponseShapeIsUnchangedForExistingColumns()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User, "Manager");

        await client.PostJsonAsync(
            $"/api/boards/{seed.Board.Id}/smart-setup/confirm",
            new
            {
                description = "x",
                summary = "y",
                columnId = seed.Todo.Id,
                tasks = new object[] { new { title = "Việc A" } },
            });

        var response = await client.GetAsync($"/api/workspaces/{seed.Workspace.Id}/ai-actions");
        var raw = await response.Content.ReadFromJsonAsync<JsonElement>();

        var first = raw[0];

        // The workspace list returns exactly the same AiActionLogResponse shape as the board-scoped list, so
        // the existing drawer/item components can render it without a second DTO.
        foreach (var field in new[]
                 {
                     "id", "action", "entityType", "entityId", "status",
                     "requestedByUserId", "taskCount", "createdAt", "updatedAt",
                 })
        {
            Assert.True(first.TryGetProperty(field, out _), $"Missing '{field}' in the response shape.");
        }
    }

    // ---- helpers ----------------------------------------------------------

    private sealed record Seed(ApplicationUser User, Workspace Workspace, Board Board, BoardColumn Todo, BoardColumn Done);

    private static async Task<Seed> SeedAsync(TestScenario scenario)
    {
        var user = await scenario.CreateUserAsync("Chủ workspace");
        var workspace = await scenario.CreateWorkspaceAsync(user, "Workspace lịch sử AI");
        var (board, todo, done) = await scenario.CreateBoardAsync(workspace);

        return new Seed(user, workspace, board, todo, done);
    }

    private sealed record AiActionLogDto(
        Guid Id,
        string Action,
        string EntityType,
        Guid? EntityId,
        string Status,
        DateTimeOffset CreatedAt);
}
