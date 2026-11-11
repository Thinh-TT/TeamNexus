using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Auth.Services;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;
using TeamNexus.Shared;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Sub-phase 15.1 verification suite (<c>T15.1.1</c> → <c>T15.1.5</c>):
/// workspace-scoped sequential <c>TaskNumber</c> (<c>TN-123</c>), <c>TaskSearchService</c> exact
/// code matching, <c>BoardColumn.IsInReview</c> mutual exclusion &amp; single-per-board enforcement,
/// <c>ApplicationUser.IsSystem</c> login guard, and the <see cref="ITaskAutomation"/> port.
/// </summary>
public sealed class Phase15FoundationTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public Phase15FoundationTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task TaskCreate_AllocatesSequentialTaskNumberPerWorkspace_AndIsolatesWorkspaces()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Chủ Workspace");
        var ws1 = await scenario.CreateWorkspaceAsync(user, "Workspace 1");
        var ws2 = await scenario.CreateWorkspaceAsync(user, "Workspace 2");
        var (board1, todo1, _) = await scenario.CreateBoardAsync(ws1, "Board 1");
        var (board2, todo2, _) = await scenario.CreateBoardAsync(ws2, "Board 2");

        using var client = await scenario.AsUserAsync(user);

        var r1 = await client.PostJsonAsync(
            $"/api/boards/{board1.Id}/tasks",
            new { columnId = todo1.Id, title = "Thẻ đầu tiên WS1" });
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        var t1 = await r1.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(t1);
        Assert.Equal(1L, t1!.TaskNumber);

        var r2 = await client.PostJsonAsync(
            $"/api/boards/{board1.Id}/tasks",
            new { columnId = todo1.Id, title = "Thẻ thứ hai WS1" });
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
        var t2 = await r2.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(t2);
        Assert.Equal(2L, t2!.TaskNumber);

        // Workspace 2 starts its own independent sequence at TN-1.
        var rOther = await client.PostJsonAsync(
            $"/api/boards/{board2.Id}/tasks",
            new { columnId = todo2.Id, title = "Thẻ đầu tiên WS2" });
        Assert.Equal(HttpStatusCode.Created, rOther.StatusCode);
        var tOther = await rOther.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(tOther);
        Assert.Equal(1L, tOther!.TaskNumber);

        // Verify task_numbers table entries exist for both workspaces.
        await scenario.WithDbContextAsync(async db =>
        {
            var ws1Numbers = await db.TaskNumbers
                .Where(x => x.WorkspaceId == ws1.Id)
                .OrderBy(x => x.TaskNumber)
                .Select(x => x.TaskNumber)
                .ToListAsync();
            Assert.Equal([1L, 2L], ws1Numbers);

            var ws2Numbers = await db.TaskNumbers
                .Where(x => x.WorkspaceId == ws2.Id)
                .Select(x => x.TaskNumber)
                .ToListAsync();
            Assert.Equal([1L], ws2Numbers);
        });
    }

    [Theory]
    [InlineData("TN-2")]
    [InlineData("tn-2")]
    [InlineData("#2")]
    public async Task TaskSearch_MatchesTaskNumberByCodePrefix(string query)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Người tìm kiếm");
        var ws = await scenario.CreateWorkspaceAsync(user, "Workspace Search TN");
        var (board, todo, _) = await scenario.CreateBoardAsync(ws, "Board Search");

        using var client = await scenario.AsUserAsync(user);

        await client.PostJsonAsync($"/api/boards/{board.Id}/tasks", new { columnId = todo.Id, title = "Công việc số một" });
        var createSecond = await client.PostJsonAsync($"/api/boards/{board.Id}/tasks", new { columnId = todo.Id, title = "Công việc số hai" });
        var secondTask = await createSecond.Content.ReadFromJsonAsync<TaskResponse>();
        await client.PostJsonAsync($"/api/boards/{board.Id}/tasks", new { columnId = todo.Id, title = "Công việc số ba" });

        var response = await client.GetAsync(
            $"/api/workspaces/{ws.Id}/tasks/search?q={Uri.EscapeDataString(query)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var searchResult = await response.Content.ReadFromJsonAsync<TaskSearchResponse>();
        Assert.NotNull(searchResult);
        var item = Assert.Single(searchResult!.Items);
        Assert.Equal(secondTask!.Id, item.Task.Id);
        Assert.Equal(2L, item.Task.TaskNumber);
    }

    [Fact]
    public async Task Column_IsInReview_EnforcesMutualExclusionAndSingleInReviewPerBoard()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Quản lý Cột");
        var ws = await scenario.CreateWorkspaceAsync(user, "Workspace Column");
        var (board, _, done) = await scenario.CreateBoardAsync(ws, "Board Column");

        using var client = await scenario.AsUserAsync(user);

        // 1. Turning on both isDone and isInReview is rejected with 400 Bad Request and Vietnamese message.
        var bothDoneAndReview = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new { name = "Lỗi 2 cờ", isDone = true, isInReview = true });
        Assert.Equal(HttpStatusCode.BadRequest, bothDoneAndReview.StatusCode);
        var errBody = await bothDoneAndReview.Content.ReadAsStringAsync();
        Assert.Contains("vai trò hệ thống", errBody);

        // 2. Turning on both isClarification and isInReview is also rejected with 400.
        var bothClarificationAndReview = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new { name = "Lỗi 2 cờ B", isClarification = true, isInReview = true });
        Assert.Equal(HttpStatusCode.BadRequest, bothClarificationAndReview.StatusCode);

        // 3. Updating an existing Done column to also be isInReview is rejected with 400.
        var updateDoneToReview = await client.PutJsonAsync(
            $"/api/boards/{board.Id}/columns/{done.Id}",
            new { name = "Done", isInReview = true });
        Assert.Equal(HttpStatusCode.BadRequest, updateDoneToReview.StatusCode);

        // 4. Creating a valid In Review column succeeds.
        var createReview = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new { name = "Đang duyệt PR", isInReview = true });
        Assert.Equal(HttpStatusCode.Created, createReview.StatusCode);
        var reviewCol = await createReview.Content.ReadFromJsonAsync<ColumnResponse>();
        Assert.NotNull(reviewCol);
        Assert.True(reviewCol!.IsInReview);
        Assert.False(reviewCol.IsDone);
        Assert.False(reviewCol.IsClarification);

        // 5. Creating a second In Review column on the same board is rejected with 409 Conflict.
        var secondReview = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new { name = "Đang duyệt PR 2", isInReview = true });
        Assert.Equal(HttpStatusCode.Conflict, secondReview.StatusCode);
    }

    [Fact]
    public async Task SystemAccount_GitHubBot_IsSeededAndBlockedFromInteractiveAuth()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        await scenario.WithDbContextAsync(async db =>
        {
            var bot = await db.Users.FirstOrDefaultAsync(u => u.Id == SystemUsers.GitHubBotUserId);
            Assert.NotNull(bot);
            Assert.True(bot!.IsSystem);
            Assert.Equal(SystemUsers.GitHubBotEmail, bot.Email);
            Assert.Equal(SystemUsers.GitHubBotDisplayName, bot.DisplayName);
        });

        using var scope = scenario.Services.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TeamNexus.Persistence.Data.TeamNexusDbContext>();
        var botUser = await dbContext.Users.FirstAsync(u => u.Id == SystemUsers.GitHubBotUserId);

        // 1. Direct token issuance for a system user is blocked.
        var issueEx = await Assert.ThrowsAsync<InvalidOperationException>(
            () => authService.IssueTokenPairAsync(botUser));
        Assert.Contains("Tài khoản hệ thống", issueEx.Message);

        // 2. OAuth external login matching the bot's email is blocked.
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "github-bot-provider-key"),
            new Claim(ClaimTypes.Email, SystemUsers.GitHubBotEmail),
            new Claim(ClaimTypes.Name, "GitHub Bot"),
        };
        var loginEx = await Assert.ThrowsAsync<InvalidOperationException>(
            () => authService.HandleExternalLoginAsync("GitHub", claims));
        Assert.Contains("Tài khoản hệ thống", loginEx.Message);
    }

    [Fact]
    public async Task TaskAutomation_EndToEnd_CreateFromIssue_Triage_MoveColumns_AndComment()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var owner = await scenario.CreateUserAsync("Chủ dự án");
        var ws = await scenario.CreateWorkspaceAsync(owner, "Workspace Automation");
        var (board, todo, done) = await scenario.CreateBoardAsync(ws, "Board Automation");

        using var client = await scenario.AsUserAsync(owner);
        var createReview = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new { name = "In Review", isInReview = true });
        var reviewCol = await createReview.Content.ReadFromJsonAsync<ColumnResponse>();
        Assert.NotNull(reviewCol);

        using var scope = scenario.Services.CreateScope();
        var automation = scope.ServiceProvider.GetRequiredService<ITaskAutomation>();

        // 1. Create task from external issue -> lands in first non-system column (todo) with TaskNumber = 1.
        var created = await automation.CreateTaskFromExternalIssueAsync(
            board.Id,
            "[GH #42] Sửa lỗi xác thực webhook",
            "Mô tả chi tiết từ GitHub Issue #42");
        Assert.Equal(1L, created.TaskNumber);
        Assert.Equal(todo.Id, created.ColumnId);

        // 2. Apply system triage suggestions (priority, labels, assignee).
        var triaged = await automation.ApplySystemTriageSuggestionsAsync(
            created.Id,
            priority: "Urgent",
            estimatedHours: 4m,
            tags: ["security", "backend"],
            assigneeId: owner.Id);
        Assert.Equal("Urgent", triaged.Priority);
        Assert.Equal(owner.Id, triaged.AssigneeId);
        Assert.Equal(2, triaged.Labels.Count);

        // 3. Move to In Review column when PR is opened.
        var moveReview = await automation.MoveToSystemColumnAsync(
            created.Id,
            SystemColumnTarget.InReview,
            "GitHub PR #99 opened");
        Assert.True(moveReview.Moved);
        Assert.Equal(todo.Id, moveReview.FromColumnId);
        Assert.Equal(reviewCol!.Id, moveReview.ToColumnId);
        Assert.Null(moveReview.NewCompletedAt);

        // 4. Move to Done column when PR is merged -> sets CompletedAt.
        var moveDone = await automation.MoveToSystemColumnAsync(
            created.Id,
            SystemColumnTarget.Done,
            "GitHub PR #99 merged");
        Assert.True(moveDone.Moved);
        Assert.Equal(reviewCol.Id, moveDone.FromColumnId);
        Assert.Equal(done.Id, moveDone.ToColumnId);
        Assert.NotNull(moveDone.NewCompletedAt);

        // 5. Add system comment under GitHub Bot.
        var comment = await automation.AddSystemCommentAsync(
            created.Id,
            SystemUsers.GitHubBotUserId,
            "💬 **@octocat** đã bình luận trên GitHub (#42): Đã kiểm tra xong.");
        Assert.Equal(SystemUsers.GitHubBotUserId, comment.AuthorId);
        Assert.Equal(SystemUsers.GitHubBotDisplayName, comment.AuthorName);
    }

    [Fact]
    public async Task Schema_Migration15AndBackfillSql_WorkAsDesigned()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Kiểm định Schema 15.1");
        var ws = await scenario.CreateWorkspaceAsync(user, "Workspace Backfill");
        var (board, todo, _) = await scenario.CreateBoardAsync(ws, "Board Backfill");

        var olderTask = new BoardTask
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            BoardId = board.Id,
            ColumnId = todo.Id,
            Title = "Thẻ cũ 1",
            Priority = TaskPriority.Medium,
            Position = 0,
            TaskNumber = 0,
            CreatedBy = user.Id,
        };
        var newerTask = new BoardTask
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            BoardId = board.Id,
            ColumnId = todo.Id,
            Title = "Thẻ cũ 2",
            Priority = TaskPriority.High,
            Position = 1,
            TaskNumber = 0,
            CreatedBy = user.Id,
        };

        await scenario.WithDbContextAsync(async db =>
        {
            db.Tasks.AddRange(olderTask, newerTask);
            await db.SaveChangesAsync();

            // Execute the exact migration backfill SQL on tasks with task_number = 0.
            await db.Database.ExecuteSqlRawAsync(
                """
                WITH numbered AS (
                    SELECT t.id AS task_id,
                           b.workspace_id,
                           t.created_at,
                           ROW_NUMBER() OVER (PARTITION BY b.workspace_id ORDER BY t.created_at ASC, t.id ASC) AS seq_num
                    FROM tasks t
                    INNER JOIN boards b ON b.id = t.board_id
                )
                UPDATE tasks AS t
                SET task_number = n.seq_num
                FROM numbered AS n
                WHERE t.id = n.task_id;

                INSERT INTO task_numbers (workspace_id, task_number, task_id, created_at)
                SELECT b.workspace_id, t.task_number, t.id, t.created_at
                FROM tasks t
                INNER JOIN boards b ON b.id = t.board_id
                WHERE t.task_number > 0
                ON CONFLICT DO NOTHING;

                UPDATE workspaces AS w
                SET next_task_number = COALESCE(
                    (SELECT MAX(tn.task_number) + 1 FROM task_numbers tn WHERE tn.workspace_id = w.id),
                    1
                );
                """);
        });

        await scenario.WithDbContextAsync(async db =>
        {
            var t1 = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == olderTask.Id);
            var t2 = await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == newerTask.Id);
            var updatedWs = await db.Workspaces.AsNoTracking().FirstAsync(w => w.Id == ws.Id);

            Assert.Equal(1L, t1.TaskNumber);
            Assert.Equal(2L, t2.TaskNumber);
            Assert.Equal(3L, updatedWs.NextTaskNumber);
        });
    }
}

