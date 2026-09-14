using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 11 §6 — the notification triggers that make the header bell useful: a task assigned to you,
/// and a comment on a task you are responsible for.
/// <para>
/// The infrastructure already existed (Phase 5/7); what is new is that Board now emits the events
/// through the <c>INotificationWriter</c> port. The tests therefore focus on the <b>rules</b>: who is
/// notified, who is deliberately <i>not</i>, and the fact that editing a task without changing its
/// assignee must not send anything.
/// </para>
/// <para>
/// Every write goes through HTTP (not EF seeding) on purpose: the triggers live in the service layer
/// behind the endpoints, so a directly-inserted row would prove nothing (the T1 trap from
/// Phase 10 §2.7).
/// </para>
/// </summary>
public sealed class NotificationTriggerApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public NotificationTriggerApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- task assignment ---------------------------------------------------

    [Fact]
    public async Task CreatingATaskForSomebodyElse_NotifiesThemOnce()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, member, _) = await SeedBoardAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var response = await client.PostJsonAsync(
            $"/api/boards/{workspace.Board.Id}/tasks",
            new
            {
                title = "Chuẩn bị tài liệu",
                columnId = workspace.Todo.Id,
                assigneeId = member.Id,
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var db = scenario.NewDbContext();
        var rows = await db.Notifications.ToListAsync();

        var row = Assert.Single(rows);
        Assert.Equal(member.Id, row.RecipientUserId);
        Assert.Equal(MemberNotificationTypes.TaskAssigned, row.Type);
        Assert.Equal("Bạn được giao một thẻ mới", row.Title);
        Assert.Equal("Chuẩn bị tài liệu", row.Message);
        Assert.False(row.IsRead);

        // The payload is what lets the drawer open the task.
        using var payload = JsonDocument.Parse(row.Payload!);
        Assert.Equal(workspace.Board.Id, payload.RootElement.GetProperty("boardId").GetGuid());
        Assert.True(payload.RootElement.TryGetProperty("taskId", out _));
    }

    [Fact]
    public async Task CreatingAnUnassignedTask_NotifiesNobody()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _, _) = await SeedBoardAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        await client.PostJsonAsync(
            $"/api/boards/{workspace.Board.Id}/tasks",
            new { title = "Chưa giao ai", columnId = workspace.Todo.Id });

        await using var db = scenario.NewDbContext();
        Assert.Equal(0, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task AssigningATaskToYourself_NotifiesNobody()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _, _) = await SeedBoardAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        await client.PostJsonAsync(
            $"/api/boards/{workspace.Board.Id}/tasks",
            new { title = "Việc của tôi", columnId = workspace.Todo.Id, assigneeId = manager.Id });

        await using var db = scenario.NewDbContext();
        Assert.Equal(0, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task ReassigningATask_NotifiesOnlyTheNewAssignee()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, first, second) = await SeedBoardAsync(scenario, withSecondMember: true);
        using var client = await scenario.AsUserAsync(manager);

        var created = await (await client.PostJsonAsync(
            $"/api/boards/{workspace.Board.Id}/tasks",
            new { title = "Đổi người", columnId = workspace.Todo.Id, assigneeId = first.Id }))
            .Content.ReadFromJsonAsync<CreatedTaskDto>();

        await using (var db = scenario.NewDbContext())
        {
            db.Notifications.RemoveRange(db.Notifications);
            await db.SaveChangesAsync();
        }

        var response = await client.PutJsonAsync(
            $"/api/boards/{workspace.Board.Id}/tasks/{created!.Id}",
            new
            {
                title = "Đổi người",
                description = (string?)null,
                assigneeId = second!.Id,
                dueDate = (DateTimeOffset?)null,
                priority = (string?)null,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verify = scenario.NewDbContext();
        var row = Assert.Single(await verify.Notifications.ToListAsync());
        Assert.Equal(second.Id, row.RecipientUserId);
        Assert.Equal(MemberNotificationTypes.TaskAssigned, row.Type);
    }

    [Fact]
    public async Task EditingATaskWithoutChangingTheAssignee_NotifiesNobody()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, member, _) = await SeedBoardAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var created = await (await client.PostJsonAsync(
            $"/api/boards/{workspace.Board.Id}/tasks",
            new { title = "Tiêu đề gốc", columnId = workspace.Todo.Id, assigneeId = member.Id }))
            .Content.ReadFromJsonAsync<CreatedTaskDto>();

        // Clear the assignment alert so the assertion below is about the edit alone.
        await using (var db = scenario.NewDbContext())
        {
            db.Notifications.RemoveRange(db.Notifications);
            await db.SaveChangesAsync();
        }

        // Same assignee, everything else different: this must NOT re-notify (no spam on every save).
        var response = await client.PutJsonAsync(
            $"/api/boards/{workspace.Board.Id}/tasks/{created!.Id}",
            new
            {
                title = "Tiêu đề mới",
                description = "Mô tả mới",
                assigneeId = member.Id,
                dueDate = (DateTimeOffset?)null,
                priority = "High",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verify = scenario.NewDbContext();
        Assert.Equal(0, await verify.Notifications.CountAsync());
    }

    // ---- comments ----------------------------------------------------------

    [Fact]
    public async Task CommentingOnAnAssignedTask_NotifiesTheAssignee()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, member, _) = await SeedBoardAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var created = await (await client.PostJsonAsync(
            $"/api/boards/{workspace.Board.Id}/tasks",
            new { title = "Có bình luận", columnId = workspace.Todo.Id, assigneeId = member.Id }))
            .Content.ReadFromJsonAsync<CreatedTaskDto>();

        await using (var db = scenario.NewDbContext())
        {
            db.Notifications.RemoveRange(db.Notifications);
            await db.SaveChangesAsync();
        }

        var response = await client.PostJsonAsync(
            $"/api/tasks/{created!.Id}/comments",
            new { content = "Nhớ kiểm tra lại phần mở đầu." });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verify = scenario.NewDbContext();
        var row = Assert.Single(await verify.Notifications.ToListAsync());
        Assert.Equal(member.Id, row.RecipientUserId);
        Assert.Equal(MemberNotificationTypes.CommentOnTask, row.Type);

        // The message previews the comment (author + an excerpt) instead of copying it into a
        // long-lived, queryable row.
        Assert.Contains("Nhớ kiểm tra lại", row.Message);
        Assert.True(row.Message.Length <= 2000);
    }

    [Fact]
    public async Task CommentingOnAnUnassignedTask_NotifiesNobody()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _, _) = await SeedBoardAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var created = await (await client.PostJsonAsync(
            $"/api/boards/{workspace.Board.Id}/tasks",
            new { title = "Chưa giao", columnId = workspace.Todo.Id }))
            .Content.ReadFromJsonAsync<CreatedTaskDto>();

        await client.PostJsonAsync($"/api/tasks/{created!.Id}/comments", new { content = "Ai đó?" });

        await using var db = scenario.NewDbContext();
        Assert.Equal(0, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task CommentingOnYourOwnTask_NotifiesNobody()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, member, _) = await SeedBoardAsync(scenario);
        using var managerClient = await scenario.AsUserAsync(manager);

        var created = await (await managerClient.PostJsonAsync(
            $"/api/boards/{workspace.Board.Id}/tasks",
            new { title = "Việc của tôi", columnId = workspace.Todo.Id, assigneeId = member.Id }))
            .Content.ReadFromJsonAsync<CreatedTaskDto>();

        await using (var db = scenario.NewDbContext())
        {
            db.Notifications.RemoveRange(db.Notifications);
            await db.SaveChangesAsync();
        }

        // The assignee comments on their own task: they already know.
        using var memberClient = await scenario.AsUserAsync(member);
        await memberClient.PostJsonAsync(
            $"/api/tasks/{created!.Id}/comments",
            new { content = "Tôi đang làm." });

        await using var verify = scenario.NewDbContext();
        Assert.Equal(0, await verify.Notifications.CountAsync());
    }

    [Fact]
    public async Task ATruncatedCommentPreview_StaysWithinTheMessageColumn()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, member, _) = await SeedBoardAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var created = await (await client.PostJsonAsync(
            $"/api/boards/{workspace.Board.Id}/tasks",
            new { title = "Bình luận dài", columnId = workspace.Todo.Id, assigneeId = member.Id }))
            .Content.ReadFromJsonAsync<CreatedTaskDto>();

        await using (var db = scenario.NewDbContext())
        {
            db.Notifications.RemoveRange(db.Notifications);
            await db.SaveChangesAsync();
        }

        await client.PostJsonAsync(
            $"/api/tasks/{created!.Id}/comments",
            new { content = new string('z', 4000) });

        await using var verify = scenario.NewDbContext();
        var row = Assert.Single(await verify.Notifications.ToListAsync());

        // 120-char excerpt + author prefix, never the whole 4000-char comment.
        Assert.True(row.Message.Length < 500, $"message was {row.Message.Length}");
        Assert.DoesNotContain(new string('z', 200), row.Message);
    }

    // ---- read path regression ----------------------------------------------

    [Fact]
    public async Task TheAlertsArePrivateAndReadableOnlyByTheirRecipient()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, member, _) = await SeedBoardAsync(scenario);
        using var managerClient = await scenario.AsUserAsync(manager);

        var created = await (await managerClient.PostJsonAsync(
            $"/api/boards/{workspace.Board.Id}/tasks",
            new { title = "Riêng tư", columnId = workspace.Todo.Id, assigneeId = member.Id }))
            .Content.ReadFromJsonAsync<CreatedTaskDto>();

        Assert.NotNull(created);

        // ---- as the assignee: the alert is there ----
        using var memberClient = await scenario.AsUserAsync(member);
        var mine = await memberClient.GetJsonAsync<NotificationListDto>("/api/notifications");

        Assert.NotNull(mine);
        Assert.Equal(1, mine!.UnreadCount);
        var alert = Assert.Single(mine.Items);
        Assert.Equal(MemberNotificationTypes.TaskAssigned, alert.Type);

        var alertId = alert.Id;

        // ---- as the Manager: nothing, and no way to touch it ----
        //
        // `AsUserAsync(member)` above OVERWROTE the scenario's shared cookie jar, so `managerClient`
        // — which is only a wrapper around that jar (SessionCookieHandler) — would now authenticate as
        // the member. Re-signing in is not decoration: without it these assertions silently test the
        // member twice, which is exactly how this test first failed in CI.
        using var freshManagerClient = await scenario.AsUserAsync(manager);

        var theirs = await freshManagerClient.GetJsonAsync<NotificationListDto>("/api/notifications");
        Assert.NotNull(theirs);
        Assert.Equal(0, theirs!.UnreadCount);
        Assert.Empty(theirs.Items);

        // Marking somebody else's alert read must look like it does not exist (Phase 5 invariant).
        //
        // Two traps meet on this line, and both cost a CI run:
        //   1. `AsUserAsync(member)` overwrote the scenario's shared cookie jar, so the original
        //      `managerClient` was authenticating as the MEMBER from that point on — hence
        //      `freshManagerClient`, created just above.
        //   2. The cached antiforgery token still belongs to the member session, and ASP.NET Core
        //      binds that token to the identity that requested it — so this POST first failed with
        //      **403** (CSRF), not the 404 under test. Hence the fresh-token helper.
        var foreign = await scenario.PostWithFreshAntiforgeryAsync(
            freshManagerClient, $"/api/notifications/{alertId}/read");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        // The recipient themselves can read it — with a token minted for THEIR session.
        var own = await scenario.PostWithFreshAntiforgeryAsync(
            memberClient, $"/api/notifications/{alertId}/read");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    // ---- helpers -----------------------------------------------------------

    private static async Task<(
        ApplicationUser Manager, SeedContext Workspace, ApplicationUser Member, ApplicationUser? Second)>
        SeedBoardAsync(TestScenario scenario, bool withSecondMember = false)
    {
        var manager = await scenario.CreateUserAsync("Quản lý", "nt.manager@example.test");
        var member = await scenario.CreateUserAsync("Người phụ trách", "nt.assignee@example.test");
        var second = withSecondMember
            ? await scenario.CreateUserAsync("Người thứ hai", "nt.second@example.test")
            : null;

        var workspace = second is null
            ? await scenario.CreateWorkspaceAsync(manager, "Workspace Thông Báo", (member, WorkspaceRole.Member))
            : await scenario.CreateWorkspaceAsync(
                manager,
                "Workspace Thông Báo",
                (member, WorkspaceRole.Member),
                (second, WorkspaceRole.Member));

        var (board, todo, done) = await scenario.CreateBoardAsync(workspace);

        return (manager, new SeedContext(workspace, board, todo, done), member, second);
    }

    /// <summary>The seeded workspace plus its board and columns, kept together for readability.</summary>
    private sealed record SeedContext(
        Workspace Workspace, Board Board, BoardColumn Todo, BoardColumn Done);

    private sealed record CreatedTaskDto
    {
        public Guid Id { get; init; }
        public string? Title { get; init; }
        public Guid? AssigneeId { get; init; }
    }

    private sealed record NotificationItemDto
    {
        public Guid Id { get; init; }
        public string? Type { get; init; }
        public string? Title { get; init; }
        public string? Message { get; init; }
        public bool IsRead { get; init; }
    }

    private sealed record NotificationListDto
    {
        public int UnreadCount { get; init; }
        public List<NotificationItemDto> Items { get; init; } = [];
    }
}
