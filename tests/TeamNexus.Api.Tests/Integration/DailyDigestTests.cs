using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Ai.Services.Email;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 13 §3.2/§3.3 — the daily digest: content assembly (DA-1…DA-6) and the send pass (DR-1…DR-4).
/// <para>
/// <b>Why a private host:</b> the digest is off by default (deliberately — it is the only feature that
/// e-mails people unprompted), so these suites build their own host with <c>Digest:Enabled=true</c> instead
/// of flipping the switch for every other suite.
/// </para>
/// <para>
/// <b>Why the runner is called directly:</b> <see cref="IDailyDigestRunner"/> is a scoped service precisely
/// so a test can run one pass without a timer, a startup delay or a sleep. The clock is frozen with
/// <see cref="FixedTimeProvider"/> so "which day is it" and "has the send hour arrived" are facts of the
/// test, not of when it ran.
/// </para>
/// </summary>
public sealed class DailyDigestTests : IClassFixture<DatabaseFixture>
{
    /// <summary>
    /// Frozen "now": 2026-02-01 12:00 UTC. With the digest timezone at UTC+7 that is 19:00 local on
    /// 2026-02-01 — comfortably past a <c>SendAtLocalHour = 0</c> send time, so "today's digest is due" is
    /// true for every test here.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly TodayLocal = new(2026, 2, 1);

    /// <summary>A due date clearly in the past relative to <see cref="Now"/> ⇒ the task is overdue.</summary>
    private static readonly DateTimeOffset OverdueDue = new(2026, 1, 25, 0, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture _database;

    public DailyDigestTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- DA-1 … DA-2: content assembly -------------------------------------

    [Fact]
    public async Task DA1_UserWithAnOverdueTask_GetsOneSectionWithThatBucket()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var seed = await SeedAsync(scenario);

        await SeedTaskAsync(scenario, seed, "Trễ hạn rồi", dueDate: OverdueDue);

        var content = await BuildAsync(scenario.Services, seed.User.Id);

        Assert.NotNull(content);
        Assert.Equal("reader@example.test", content!.RecipientEmail);
        Assert.Equal(TodayLocal, content.SendDateLocal);
        Assert.Single(content.Workspaces);

        var dashboard = content.Workspaces[0].Dashboard;
        Assert.Equal(seed.Workspace.Name, dashboard.WorkspaceName);
        Assert.Equal(1, dashboard.MyTasks.Overdue.Count);
        Assert.Equal("Trễ hạn rồi", dashboard.MyTasks.Overdue.Items[0].Title);
        Assert.Equal(1, dashboard.Summary.MyOpenTasks);

        // Both deep links point at the frontend that was configured for the host.
        Assert.Contains($"/workspaces/{seed.Workspace.Id:D}/dashboard", content.DashboardUrl);
        Assert.Contains("/profile#notifications", content.UnsubscribeUrl);
    }

    [Fact]
    public async Task DA2_BucketTotalStaysExactWhenTheListingIsCapped()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var seed = await SeedAsync(scenario);

        for (var i = 0; i < 7; i++)
        {
            await SeedTaskAsync(scenario, seed, $"Quá hạn {i}", dueDate: OverdueDue);
        }

        var content = await BuildAsync(scenario.Services, seed.User.Id, maxItemsPerBucket: 2);

        Assert.NotNull(content);
        var overdue = content!.Workspaces[0].Dashboard.MyTasks.Overdue;

        // The count is the truth; only the listing is capped — so the email can say "… và 5 thẻ khác".
        Assert.Equal(7, overdue.Count);
        Assert.Equal(2, overdue.Items.Count);
    }

    // ---- DA-3 … DA-4: nothing to send, and that is not an error ------------

    [Fact]
    public async Task DA3_OptedOutUserProducesNothing()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var seed = await SeedAsync(scenario);
        await SeedTaskAsync(scenario, seed, "Trễ hạn rồi", dueDate: OverdueDue);

        await SetDigestEnabledAsync(scenario, seed.User.Id, enabled: false);

        Assert.Null(await BuildAsync(scenario.Services, seed.User.Id));
    }

    [Fact]
    public async Task DA4_UserWithNoOpenTaskProducesNothing()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var seed = await SeedAsync(scenario);

        // The only task is done ⇒ nothing to report, so no e-mail (an empty digest trains people to
        // ignore the one that matters).
        await SeedTaskAsync(scenario, seed, "Xong rồi", dueDate: OverdueDue, completedAt: Now.AddHours(-2));

        Assert.Null(await BuildAsync(scenario.Services, seed.User.Id));
    }

    [Fact]
    public async Task DA4b_UserWithNoWorkspaceProducesNothing()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var lonely = await scenario.CreateUserAsync("Không workspace", "lonely@example.test");

        Assert.Null(await BuildAsync(scenario.Services, lonely.Id));
    }

    // ---- DA-5: only my work, only live boards ------------------------------

    [Fact]
    public async Task DA5_OnlyMyOwnTasksOnLiveBoardsAppear()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var seed = await SeedAsync(scenario);

        var colleague = await scenario.CreateUserAsync("Đồng nghiệp", "colleague@example.test");
        await AddMemberAsync(scenario, seed.Workspace.Id, colleague.Id, WorkspaceRole.Member);

        // Mine, overdue.
        await SeedTaskAsync(scenario, seed, "Việc của tôi", dueDate: OverdueDue);

        // Someone else's overdue task: a digest is personal, never a team report.
        await SeedTaskAsync(scenario, seed, "Việc của người khác", dueDate: OverdueDue, assigneeId: colleague.Id);

        // My task, but on a soft-deleted board: the query filter must keep it out.
        var ghostBoard = await SeedSoftDeletedBoardAsync(scenario, seed, "Board đã xoá");
        await SeedTaskAsync(scenario, seed, "Trên board đã xoá", dueDate: OverdueDue, boardId: ghostBoard);

        var content = await BuildAsync(scenario.Services, seed.User.Id);

        Assert.NotNull(content);
        var titles = content!.Workspaces[0].Dashboard.MyTasks.Overdue.Items.Select(i => i.Title).ToList();

        Assert.Contains("Việc của tôi", titles);
        Assert.DoesNotContain("Việc của người khác", titles);
        Assert.DoesNotContain("Trên board đã xoá", titles);
        Assert.Equal(1, content.Workspaces[0].Dashboard.MyTasks.Overdue.Count);
    }

    // ---- DA-6: several workspaces in one mail ------------------------------

    [Fact]
    public async Task DA6_OneMailCoversEveryWorkspaceOrderedByJoinDate()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var user = await scenario.CreateUserAsync("Nhiều workspace", "reader@example.test");

        var first = await scenario.CreateWorkspaceAsync(user, "Workspace 1");
        var second = await scenario.CreateWorkspaceAsync(user, "Workspace 2");
        var third = await scenario.CreateWorkspaceAsync(user, "Workspace 3");

        foreach (var workspace in new[] { first, second, third })
        {
            var (board, todo, done) = await scenario.CreateBoardAsync(workspace, $"Board {workspace.Name}");
            await SeedTaskAsync(
                scenario,
                new Seed(user, workspace, board, todo, done),
                $"Việc ở {workspace.Name}",
                dueDate: OverdueDue);
        }

        var content = await BuildAsync(scenario.Services, user.Id);

        Assert.NotNull(content);
        Assert.Equal(3, content!.Workspaces.Count);

        // Deterministic order (joined_at ASC) so two runs produce the same mail.
        Assert.Equal(
            new[] { "Workspace 1", "Workspace 2", "Workspace 3" },
            content.Workspaces.Select(s => s.Dashboard.WorkspaceName).ToArray());

        // …and one mail in total, not three: the digest is per person, not per workspace.
        Assert.Contains($"/workspaces/{first.Id:D}/dashboard", content.DashboardUrl);
    }

    [Fact]
    public async Task DA6b_WorkspaceCapIsRespected()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var user = await scenario.CreateUserAsync("Nhiều workspace", "reader@example.test");

        for (var i = 0; i < 3; i++)
        {
            var workspace = await scenario.CreateWorkspaceAsync(user, $"Workspace {i}");
            var (board, todo, done) = await scenario.CreateBoardAsync(workspace, $"Board {i}");
            await SeedTaskAsync(
                scenario,
                new Seed(user, workspace, board, todo, done),
                $"Việc {i}",
                dueDate: OverdueDue);
        }

        var content = await BuildAsync(scenario.Services, user.Id, maxWorkspaces: 2);

        Assert.NotNull(content);
        Assert.Equal(2, content!.Workspaces.Count);
    }

    // ---- DR-1: the off switch ----------------------------------------------

    [Fact]
    public async Task DR1_WhenDisabled_NothingIsSentAndNoAuditRowAppears()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        await SeedTaskAsync(scenario, seed, "Trễ hạn rồi", dueDate: OverdueDue);

        // Host WITHOUT DigestEnabled ⇒ the production default (off).
        using var host = new TeamNexusApiFactory
        {
            ConnectionString = TestScenario.ConnectionStringForTests,
            Clock = new FixedTimeProvider(Now),
        };

        var result = await RunAsync(host.Services, scenario);

        Assert.True(result.Skipped);
        Assert.Equal(0, await scenario.CountAsync<EmailMessage>(m => m.Kind == EmailKinds.DailyDigest));
    }

    // ---- DR-2: the happy path ----------------------------------------------

    [Fact]
    public async Task DR2_EachEligibleUserGetsExactlyOneAuditRowAndItIsSent()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var seed = await SeedAsync(scenario);
        await SeedTaskAsync(scenario, seed, "Trễ hạn rồi", dueDate: OverdueDue);

        var second = await scenario.CreateUserAsync("Người thứ hai", "second@example.test");
        await AddMemberAsync(scenario, seed.Workspace.Id, second.Id, WorkspaceRole.Member);
        await SeedTaskAsync(scenario, seed, "Việc của người thứ hai", dueDate: OverdueDue, assigneeId: second.Id);

        using var host = DigestHost();
        var result = await RunAsync(host.Services, scenario);

        Assert.False(result.Skipped);
        Assert.Equal(2, result.Sent);
        Assert.Equal(0, result.Failed);

        Assert.Equal(2, await scenario.CountAsync<EmailMessage>(m => m.Kind == EmailKinds.DailyDigest));

        // The offline sender reports success (`NullEmailSender` ⇒ `EmailSendResult.Sent(null)`), which is
        // what lets the whole pipeline be verified without a Resend key — hence a real Sent row with no
        // provider id.
        var rows = await LoadDigestRowsAsync(scenario);
        Assert.All(rows, row =>
        {
            Assert.Equal(EmailMessageStatus.Sent, row.Status);
            Assert.Null(row.ProviderMessageId);
            Assert.Null(row.SentByUserId); // nobody pressed a button
        });
    }

    // ---- DR-3: idempotency (the important one) ------------------------------

    [Fact]
    public async Task DR3_ASecondPassOnTheSameDaySendsNothing()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var seed = await SeedAsync(scenario);
        await SeedTaskAsync(scenario, seed, "Trễ hạn rồi", dueDate: OverdueDue);

        var host = DigestHost();

        var first = await RunAsync(host.Services, scenario);
        var second = await RunAsync(host.Services, scenario);

        Assert.Equal(1, first.Sent);
        Assert.True(second.Sent == 0, "Lượt thứ hai trong cùng ngày không được gửi thêm email.");
        Assert.Equal(1, second.SkippedAlreadySent);

        // Exactly one row, after two passes. This is what protects a free-tier host that sleeps and
        // restarts several times a day: the "already sent" state lives in the database, not in memory.
        Assert.Equal(1, await scenario.CountAsync<EmailMessage>(m => m.Kind == EmailKinds.DailyDigest));
    }

    // ---- DR-4: who is skipped -----------------------------------------------

    [Fact]
    public async Task DR4_OptedOutAndAddresslessUsersAreBothSkipped()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var seed = await SeedAsync(scenario);
        await SeedTaskAsync(scenario, seed, "Trễ hạn rồi", dueDate: OverdueDue);

        // Opted out.
        await SetDigestEnabledAsync(scenario, seed.User.Id, enabled: false);

        // An AI Agent pseudo-member: a real `users` row with no e-mail address (Phase 7).
        var agent = await scenario.CreateUserAsync("TeamNexus Agent", email: null!);
        await SetEmailNullAsync(scenario, agent.Id);
        await AddMemberAsync(scenario, seed.Workspace.Id, agent.Id, WorkspaceRole.Member);

        using var host = DigestHost();
        var result = await RunAsync(host.Services, scenario);

        Assert.False(result.Skipped);
        Assert.Equal(0, result.Sent);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, await scenario.CountAsync<EmailMessage>(m => m.Kind == EmailKinds.DailyDigest));
    }

    [Fact]
    public async Task DR4b_UserWithNothingToReportIsSkippedWithoutAnEmail()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var seed = await SeedAsync(scenario);
        await SeedTaskAsync(scenario, seed, "Xong rồi", dueDate: OverdueDue, completedAt: Now.AddHours(-2));

        using var host = DigestHost();
        var result = await RunAsync(host.Services, scenario);

        Assert.Equal(0, result.Sent);
        Assert.Equal(1, result.SkippedEmpty);
        Assert.Equal(0, await scenario.CountAsync<EmailMessage>(m => m.Kind == EmailKinds.DailyDigest));
    }

    // ---- the digest must not become an event source -------------------------

    [Fact]
    public async Task DigestWritesNoNotificationAndNoActivityLog()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var seed = await SeedAsync(scenario);
        await SeedTaskAsync(scenario, seed, "Trễ hạn rồi", dueDate: OverdueDue);

        // The seeding itself writes nothing to either table, so a non-zero count here would be the digest.
        Assert.Equal(0, await scenario.CountAsync<Notification>());
        Assert.Equal(0, await scenario.CountAsync<ActivityLog>());

        using var host = DigestHost();
        var result = await RunAsync(host.Services, scenario);
        Assert.Equal(1, result.Sent);

        // A digest is a scheduled re-read of data the user can already see. If it started emitting
        // notifications or activity rows it would become an event source of its own, and every dashboard
        // and activity page would begin reporting the digest as if something had happened.
        Assert.Equal(0, await scenario.CountAsync<Notification>());
        Assert.Equal(0, await scenario.CountAsync<ActivityLog>());
    }

    // ---- helpers: running the pass ------------------------------------------

    /// <summary>A host with the digest switched on, the send hour at 00:00 and the clock frozen.</summary>
    private static TeamNexusApiFactory DigestHost() => new()
    {
        ConnectionString = TestScenario.ConnectionStringForTests,
        DigestEnabled = true,
        DigestSendAtLocalHour = 0,
        Clock = new FixedTimeProvider(Now),
    };

    /// <summary>
    /// Runs one digest pass on <paramref name="host"/>. The scenario is passed only to keep the database lock
    /// held for the duration — it is the same server, so the run sees the seeded rows.
    /// </summary>
    private static async Task<DigestRunResult> RunAsync(IServiceProvider host, TestScenario scenario)
    {
        await using var scope = host.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IDailyDigestRunner>();

        _ = scenario;

        return await runner.RunOnceAsync();
    }

    private static async Task<DailyDigestContent?> BuildAsync(
        IServiceProvider host,
        Guid userId,
        int maxWorkspaces = 10,
        int maxItemsPerBucket = 5)
    {
        await using var scope = host.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDailyDigestService>();

        return await service.BuildAsync(userId, TodayLocal, maxWorkspaces, maxItemsPerBucket);
    }

    private static async Task<List<EmailMessage>> LoadDigestRowsAsync(TestScenario scenario)
    {
        await using var db = scenario.NewDbContext();
        return await db.EmailMessages
            .AsNoTracking()
            .Where(m => m.Kind == EmailKinds.DailyDigest)
            .ToListAsync();
    }

    // ---- helpers: seeding ---------------------------------------------------

    private async Task<Seed> SeedAsync(TestScenario scenario)
    {
        var user = await scenario.CreateUserAsync("Người nhận", "reader@example.test");
        var workspace = await scenario.CreateWorkspaceAsync(user, "Workspace digest");
        var (board, todo, done) = await scenario.CreateBoardAsync(workspace, "Board chính");

        return new Seed(user, workspace, board, todo, done);
    }

    /// <summary>
    /// Inserts an overdue (or completed) task directly and forces <c>created_at</c>.
    /// <para>
    /// The post-insert UPDATE exists for the same reason as in <c>DashboardApiTests</c>:
    /// <c>SaveChanges</c> stamps the audit columns with the real wall clock, so a task "created last week"
    /// would otherwise always land at now.
    /// </para>
    /// </summary>
    private static async Task SeedTaskAsync(
        TestScenario scenario,
        Seed seed,
        string title,
        DateTimeOffset? dueDate = null,
        DateTimeOffset? completedAt = null,
        Guid? assigneeId = null,
        Guid? boardId = null,
        Guid? columnId = null)
    {
        await using var db = scenario.NewDbContext();

        var createdAt = Now.AddDays(-10);

        var task = new BoardTask
        {
            Id = Guid.NewGuid(),
            BoardId = boardId ?? seed.Board.Id,
            ColumnId = columnId ?? seed.Todo.Id,
            Title = title,
            Position = 0,
            CreatedBy = seed.User.Id,
            AssigneeId = assigneeId ?? seed.User.Id,
            DueDate = dueDate,
            CompletedAt = completedAt,
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE tasks SET created_at = {0}, updated_at = {0} WHERE id = {1}",
            createdAt,
            task.Id);
    }

    /// <summary>A board the suite then soft-deletes, so the digest's query filter is exercised.</summary>
    private static async Task<Guid> SeedSoftDeletedBoardAsync(TestScenario scenario, Seed seed, string name)
    {
        await using var db = scenario.NewDbContext();

        var board = new Board
        {
            Id = Guid.NewGuid(),
            WorkspaceId = seed.Workspace.Id,
            Name = name,
            DeletedAt = Now.AddDays(-1),
        };

        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Name = "Todo",
            Position = 0,
            IsDone = false,
        };

        db.Boards.Add(board);
        db.BoardColumns.Add(column);
        await db.SaveChangesAsync();

        return board.Id;
    }

    private static async Task AddMemberAsync(
        TestScenario scenario, Guid workspaceId, Guid userId, WorkspaceRole role)
    {
        await using var db = scenario.NewDbContext();
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspaceId,
            UserId = userId,
            Role = role,
        });

        await db.SaveChangesAsync();
    }

    private static async Task SetDigestEnabledAsync(TestScenario scenario, Guid userId, bool enabled)
    {
        await using var db = scenario.NewDbContext();
        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.DigestEnabled, enabled));
    }

    /// <summary>
    /// Gives a user no e-mail address. <c>CreateUserAsync</c> always invents one, but an AI Agent
    /// pseudo-member in production has <c>email = null</c>, and the digest must skip it.
    /// </summary>
    private static async Task SetEmailNullAsync(TestScenario scenario, Guid userId)
    {
        await using var db = scenario.NewDbContext();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE users SET email = NULL, normalized_email = NULL WHERE id = {0}",
            userId);
    }

    /// <summary>The workspace under test plus its board.</summary>
    private sealed record Seed(
        ApplicationUser User,
        Workspace Workspace,
        Board Board,
        BoardColumn Todo,
        BoardColumn Done);
}
