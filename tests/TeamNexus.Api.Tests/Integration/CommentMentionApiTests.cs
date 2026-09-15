using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 12 §3 — <c>@mention</c> in a comment (M-1…M-10).
/// <para>
/// The rule under test is not "the text contains an @": the server <b>never parses the text</b>
/// (decision D11). What it does is take the ids the client sent, prove each one is a human member of
/// the workspace, and turn that into alerts — so every assertion here is about <i>who ends up with a
/// notification row</i>, written through HTTP because that is where the trigger lives.
/// </para>
/// <para>
/// The pre-existing assignee alert (Phase 11 §6.6) must survive untouched, which is what M-1 and M-4
/// pin down: a mention is an <b>extra</b> alert, never a replacement.
/// </para>
/// </summary>
public sealed class CommentMentionApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public CommentMentionApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task MentioningAColleague_WritesTheMentionAlertAlongsideTheAssigneeAlert()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (author, workspace, board, todo, _) = await SeedAsync(scenario);
        var assignee = await scenario.CreateUserAsync("Người phụ trách", "mention.assignee@example.test");
        var tagged = await scenario.CreateUserAsync("Người được nhắc", "mention.tagged@example.test");
        await AddMemberAsync(scenario, workspace.Id, assignee, WorkspaceRole.Member);
        await AddMemberAsync(scenario, workspace.Id, tagged, WorkspaceRole.Member);

        var task = await SeedTaskAsync(scenario, board, todo, author, "Thẻ có nhắc tên", assignee.Id);

        using var client = await scenario.AsUserAsync(author);
        var response = await client.PostJsonAsync(
            $"/api/tasks/{task.Id}/comments",
            new { content = "Nhờ @Người được nhắc xem lại", mentionUserIds = new[] { tagged.Id } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var rows = await LoadNotificationsAsync(scenario);
        Assert.Equal(2, rows.Count);

        var mention = Assert.Single(rows, r => r.Type == MemberNotificationTypes.CommentMention);
        Assert.Equal(tagged.Id, mention.RecipientUserId);
        Assert.Equal(workspace.Id, mention.WorkspaceId);
        Assert.Contains("Người được nhắc", mention.Message);
        Assert.DoesNotContain(new string('z', 200), mention.Message);

        // The Phase 11 behaviour is untouched: the assignee still gets their own alert.
        var assigned = Assert.Single(rows, r => r.Type == MemberNotificationTypes.CommentOnTask);
        Assert.Equal(assignee.Id, assigned.RecipientUserId);
    }

    [Fact]
    public async Task MentioningYourself_WritesNothing()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (author, _, board, todo, _) = await SeedAsync(scenario);
        var task = await SeedTaskAsync(scenario, board, todo, author, "Thẻ tự nhắc");

        using var client = await scenario.AsUserAsync(author);
        var response = await client.PostJsonAsync(
            $"/api/tasks/{task.Id}/comments",
            new { content = "Tôi tự nhắc @Tôi", mentionUserIds = new[] { author.Id } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Empty(await LoadNotificationsAsync(scenario));
    }

    [Fact]
    public async Task MentioningTheAssignee_DoesNotProduceASecondAlert()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (author, workspace, board, todo, _) = await SeedAsync(scenario);
        var assignee = await scenario.CreateUserAsync("Người phụ trách", "mention.dup@example.test");
        await AddMemberAsync(scenario, workspace.Id, assignee, WorkspaceRole.Member);

        var task = await SeedTaskAsync(scenario, board, todo, author, "Thẻ nhắc đúng người phụ trách", assignee.Id);

        using var client = await scenario.AsUserAsync(author);
        var response = await client.PostJsonAsync(
            $"/api/tasks/{task.Id}/comments",
            new { content = "@Người phụ trách ơi", mentionUserIds = new[] { assignee.Id } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // One person, one row: the bell must not count the same comment twice for them.
        var rows = await LoadNotificationsAsync(scenario);
        var only = Assert.Single(rows);
        Assert.Equal(assignee.Id, only.RecipientUserId);
        Assert.Equal(MemberNotificationTypes.CommentOnTask, only.Type);
    }

    [Fact]
    public async Task ACommentWithoutTheField_BehavesExactlyAsBeforePhase12()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (author, workspace, board, todo, _) = await SeedAsync(scenario);
        var assignee = await scenario.CreateUserAsync("Người phụ trách", "mention.legacy@example.test");
        await AddMemberAsync(scenario, workspace.Id, assignee, WorkspaceRole.Member);

        var task = await SeedTaskAsync(scenario, board, todo, author, "Thẻ bình luận cũ", assignee.Id);

        using var client = await scenario.AsUserAsync(author);
        var response = await client.PostJsonAsync(
            $"/api/tasks/{task.Id}/comments",
            new { content = "Bình luận không có mention" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var only = Assert.Single(await LoadNotificationsAsync(scenario));
        Assert.Equal(MemberNotificationTypes.CommentOnTask, only.Type);
    }

    [Fact]
    public async Task MentioningTheAiAgent_IsRejectedAndNothingIsWritten()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (author, workspace, board, todo, _) = await SeedAsync(scenario);
        var agent = await SeedAiAgentAsync(scenario, workspace.Id);

        var task = await SeedTaskAsync(scenario, board, todo, author, "Thẻ nhắc AI Agent");

        using var client = await scenario.AsUserAsync(author);
        var response = await client.PostJsonAsync(
            $"/api/tasks/{task.Id}/comments",
            new { content = "@Trợ lý AI giúp tôi", mentionUserIds = new[] { agent.Id } });

        // The agent has no inbox: mentioning it would write an alert nobody can ever read.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await LoadNotificationsAsync(scenario));
        Assert.Equal(0, await CountCommentsAsync(scenario, task.Id));
    }

    [Fact]
    public async Task MentioningSomebodyOutsideTheWorkspace_IsRejectedAndNothingIsWritten()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (author, _, board, todo, _) = await SeedAsync(scenario);
        var outsider = await scenario.CreateUserAsync("Người ngoài", "mention.outsider@example.test");

        var task = await SeedTaskAsync(scenario, board, todo, author, "Thẻ nhắc người ngoài");

        using var client = await scenario.AsUserAsync(author);
        var response = await client.PostJsonAsync(
            $"/api/tasks/{task.Id}/comments",
            new { content = "@Người ngoài xem giúp", mentionUserIds = new[] { outsider.Id } });

        // Letting this through would leak the existence of work the outsider cannot see.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await LoadNotificationsAsync(scenario));
        Assert.Equal(0, await CountCommentsAsync(scenario, task.Id));
    }

    [Fact]
    public async Task DuplicateIds_ProduceOneAlertPerPerson()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (author, workspace, board, todo, _) = await SeedAsync(scenario);
        var tagged = await scenario.CreateUserAsync("Người được nhắc", "mention.once@example.test");
        await AddMemberAsync(scenario, workspace.Id, tagged, WorkspaceRole.Member);

        var task = await SeedTaskAsync(scenario, board, todo, author, "Thẻ nhắc trùng");

        using var client = await scenario.AsUserAsync(author);
        var response = await client.PostJsonAsync(
            $"/api/tasks/{task.Id}/comments",
            new { content = "@Người được nhắc hai lần", mentionUserIds = new[] { tagged.Id, tagged.Id } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Single(await LoadNotificationsAsync(scenario));
    }

    [Fact]
    public async Task MoreThanTwentyMentionedPeople_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (author, workspace, board, todo, _) = await SeedAsync(scenario);
        var task = await SeedTaskAsync(scenario, board, todo, author, "Thẻ nhắc quá nhiều");

        var ids = new List<Guid>();
        for (var i = 0; i < 21; i++)
        {
            var user = await scenario.CreateUserAsync($"Thành viên {i:D2}", $"mention.many{i:D2}@example.test");
            await AddMemberAsync(scenario, workspace.Id, user, WorkspaceRole.Member);
            ids.Add(user.Id);
        }

        using var client = await scenario.AsUserAsync(author);
        var response = await client.PostJsonAsync(
            $"/api/tasks/{task.Id}/comments",
            new { content = "Nhắc cả workspace", mentionUserIds = ids });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await LoadNotificationsAsync(scenario));
    }

    [Fact]
    public async Task ElevenMentionedPeople_AllGetOneAlert_WithOnlyAPreviewOfTheComment()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (author, workspace, board, todo, _) = await SeedAsync(scenario);
        var task = await SeedTaskAsync(scenario, board, todo, author, "Thẻ nhắc mười một người");

        var ids = new List<Guid>();
        for (var i = 0; i < 11; i++)
        {
            var user = await scenario.CreateUserAsync($"Thành viên {i:D2}", $"mention.eleven{i:D2}@example.test");
            await AddMemberAsync(scenario, workspace.Id, user, WorkspaceRole.Member);
            ids.Add(user.Id);
        }

        // Long enough that the excerpt has to cut it (120 chars) while staying inside the 4000-char
        // comment limit — an over-long body would fail content validation before reaching the mention
        // path and would prove nothing about excerpts.
        var body = "Nhờ mọi người xem lại giúp: " + new string('z', 400);

        using var client = await scenario.AsUserAsync(author);
        var response = await client.PostJsonAsync(
            $"/api/tasks/{task.Id}/comments",
            new { content = body, mentionUserIds = ids });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var rows = await LoadNotificationsAsync(scenario);
        Assert.Equal(11, rows.Count);
        Assert.All(rows, r => Assert.Equal(MemberNotificationTypes.CommentMention, r.Type));
        Assert.Equal(ids.OrderBy(id => id), rows.Select(r => r.RecipientUserId).OrderBy(id => id));

        // 120-char excerpt + the author prefix, and definitely not the whole 400-char body.
        Assert.All(rows, r => Assert.True(r.Message.Length < 500, $"message was {r.Message.Length}"));
        Assert.All(rows, r => Assert.DoesNotContain(new string('z', 200), r.Message));
    }

    [Fact]
    public async Task EditingAComment_DoesNotSendTheMentionsAgain()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (author, workspace, board, todo, _) = await SeedAsync(scenario);
        var tagged = await scenario.CreateUserAsync("Người được nhắc", "mention.edit@example.test");
        await AddMemberAsync(scenario, workspace.Id, tagged, WorkspaceRole.Member);

        var task = await SeedTaskAsync(scenario, board, todo, author, "Thẻ sửa bình luận");

        using var client = await scenario.AsUserAsync(author);
        var created = await client.PostJsonAsync(
            $"/api/tasks/{task.Id}/comments",
            new { content = "@Người được nhắc lần đầu", mentionUserIds = new[] { tagged.Id } });
        created.EnsureSuccessStatusCode();

        var comment = await created.Content.ReadFromJsonAsync<CommentDto>();
        Assert.NotNull(comment);

        var edited = await client.PutJsonAsync(
            $"/api/tasks/{task.Id}/comments/{comment!.Id}",
            new { content = "@Người được nhắc lần hai (đã sửa)", mentionUserIds = new[] { tagged.Id } });

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        // Re-notifying on every edit would turn a typo fix into a second bell.
        Assert.Single(await LoadNotificationsAsync(scenario));
    }

    // ---- helpers -----------------------------------------------------------

    private static async Task<(ApplicationUser Author, Workspace Workspace, Board Board, BoardColumn Todo, BoardColumn Done)>
        SeedAsync(TestScenario scenario)
    {
        var author = await scenario.CreateUserAsync("Người bình luận");
        var workspace = await scenario.CreateWorkspaceAsync(author, "Workspace mention");
        var (board, todo, done) = await scenario.CreateBoardAsync(workspace, "Board mention");
        return (author, workspace, board, todo, done);
    }

    private static async Task AddMemberAsync(
        TestScenario scenario, Guid workspaceId, ApplicationUser user, WorkspaceRole role)
    {
        await using var db = scenario.NewDbContext();
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = user.Id, Role = role });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Adds the workspace's AI Agent. Seeded directly because promoting a member to agent is not an
    /// endpoint — the partial unique index allows exactly one per workspace, so this is the same row a
    /// real deployment would have.
    /// </summary>
    private static async Task<ApplicationUser> SeedAiAgentAsync(TestScenario scenario, Guid workspaceId)
    {
        // The agent is a real users row (Phase 7 §2.1) plus a workspace_members row of type ai_agent.
        var agentUser = await scenario.CreateUserAsync("Trợ lý AI", $"agent-{Guid.NewGuid():N}@example.test");

        await using var db = scenario.NewDbContext();
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspaceId,
            UserId = agentUser.Id,
            Role = WorkspaceRole.Member,
            MemberType = MemberType.AiAgent,
            AiAgentName = "Trợ lý AI",
        });
        await db.SaveChangesAsync();

        return agentUser;
    }

    private static async Task<BoardTask> SeedTaskAsync(
        TestScenario scenario, Board board, BoardColumn column, ApplicationUser author, string title, Guid? assigneeId = null)
    {
        await using var db = scenario.NewDbContext();

        var task = new BoardTask
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            ColumnId = column.Id,
            Title = title,
            Position = 0,
            CreatedBy = author.Id,
            AssigneeId = assigneeId,
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<List<Notification>> LoadNotificationsAsync(TestScenario scenario)
    {
        await using var db = scenario.NewDbContext();
        return await db.Notifications.AsNoTracking().ToListAsync();
    }

    private static async Task<int> CountCommentsAsync(TestScenario scenario, Guid taskId)
    {
        await using var db = scenario.NewDbContext();
        return await db.TaskComments.CountAsync(c => c.TaskId == taskId);
    }

    private sealed record CommentDto(Guid Id, Guid TaskId, Guid AuthorId, string AuthorName, string Content);
}
