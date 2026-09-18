using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 12 §1 — the workspace dashboard: "Task của tôi" buckets, board summaries, recent activity
/// and totals (D-1…D-14).
/// <para>
/// <b>Why this suite owns its host:</b> the three buckets are defined by time <i>boundaries</i>. The
/// dashboard resolves <see cref="TimeProvider"/>, so this suite injects
/// <see cref="FixedTimeProvider"/> and asserts exact edges ("due exactly now is NOT overdue") instead
/// of a fuzzily-safe middle of the range. That is the whole point of Phase 12 §P2.
/// </para>
/// <para>
/// Tasks are seeded <b>directly</b> here (not through HTTP) because the suite needs control over
/// <c>created_at</c>, which no endpoint can set. The rules under test — bucket membership and the
/// shared <c>isDone</c> definition — are read-side logic, so a directly-inserted row is a legitimate
/// fixture; the write-side rules that must go through HTTP live in the other Phase 12 suites.
/// </para>
/// </summary>
public sealed class DashboardApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    /// <summary>Frozen "now" for every test in this class. Far enough from real time that a wall-clock leak is obvious.</summary>
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    public DashboardApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- "Task của tôi" buckets -------------------------------------------

    [Fact]
    public async Task OverdueBucket_HoldsOpenTasksPastTheirDueDate()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var (me, workspace, board, todo, _) = await SeedUserWithBoardAsync(scenario, "Nhiệm vụ quá hạn");

        await SeedTaskAsync(scenario, board, todo, me, "Trễ 2 ngày", dueDate: Now.AddDays(-2));
        await SeedTaskAsync(scenario, board, todo, me, "Trễ 1 giờ", dueDate: Now.AddHours(-1));
        await SeedTaskAsync(scenario, board, todo, me, "Không hạn", dueDate: null);
        await SeedTaskAsync(scenario, board, todo, me, "Còn hạn", dueDate: Now.AddDays(1));

        using var client = await scenario.AsUserAsync(me);
        var dashboard = await client.GetJsonAsync<DashboardDto>($"/api/workspaces/{workspace.Id}/dashboard");

        Assert.NotNull(dashboard);
        Assert.Equal(2, dashboard!.MyTasks.Overdue.Count);
        Assert.Equal(2, dashboard.MyTasks.Overdue.Items.Count);

        // Oldest first: "quá hạn lâu nhất" is what a user wants to see at the top.
        Assert.Equal("Trễ 2 ngày", dashboard.MyTasks.Overdue.Items[0].Title);
        Assert.Equal("Trễ 1 giờ", dashboard.MyTasks.Overdue.Items[1].Title);

        // Whole days, minimum 1: an hour late must read as "1 ngày", never "0 ngày".
        Assert.Equal(2, dashboard.MyTasks.Overdue.Items[0].OverdueByDays);
        Assert.Equal(1, dashboard.MyTasks.Overdue.Items[1].OverdueByDays);
        Assert.Null(dashboard.MyTasks.Overdue.Items[0].Priority);
        Assert.Equal("Board chính", dashboard.MyTasks.Overdue.Items[0].BoardName);
        Assert.Equal("Todo", dashboard.MyTasks.Overdue.Items[0].ColumnName);
    }

    [Fact]
    public async Task DueDateExactlyNow_IsNotOverdue_ButIsDueSoon()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var (me, workspace, board, todo, _) = await SeedUserWithBoardAsync(scenario, "Biên dueDate");

        await SeedTaskAsync(scenario, board, todo, me, "Đúng lúc now", dueDate: Now);

        using var client = await scenario.AsUserAsync(me);
        var dashboard = await client.GetJsonAsync<DashboardDto>($"/api/workspaces/{workspace.Id}/dashboard");

        Assert.NotNull(dashboard);
        Assert.Equal(0, dashboard!.MyTasks.Overdue.Count);
        Assert.Equal(1, dashboard.MyTasks.DueSoon.Count);
        Assert.Null(dashboard.MyTasks.DueSoon.Items[0].OverdueByDays);
    }

    [Fact]
    public async Task TaskInDoneColumnWithoutCompletedAt_IsDoneAndInNoBucket()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var (me, workspace, board, _, done) = await SeedUserWithBoardAsync(scenario, "Cột done");

        // Seeded straight into the done column: completed_at stays null, exactly like a task moved
        // before Phase 7 started stamping completed_at. isDone must therefore come from the COLUMN.
        await SeedTaskAsync(scenario, board, done, me, "Xong nhưng thiếu completedAt", dueDate: Now.AddDays(-5));

        using var client = await scenario.AsUserAsync(me);
        var dashboard = await client.GetJsonAsync<DashboardDto>($"/api/workspaces/{workspace.Id}/dashboard");

        Assert.NotNull(dashboard);
        Assert.Equal(0, dashboard!.MyTasks.Overdue.Count);
        Assert.Equal(0, dashboard.MyTasks.DueSoon.Count);
        Assert.Equal(0, dashboard.MyTasks.RecentlyAssigned.Count);

        // ...but the totals still count it as done, which is what ReportAggregator does too.
        Assert.Equal(1, dashboard.Summary.TotalTasks);
        Assert.Equal(1, dashboard.Summary.DoneTasks);
        Assert.Equal(0, dashboard.Summary.OpenTasks);
        Assert.Equal(0, dashboard.Summary.OverdueTasks);
    }

    [Fact]
    public async Task DueSoon_RespectsBothEdgesOfTheWindow()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var (me, workspace, board, todo, _) = await SeedUserWithBoardAsync(scenario, "Cửa sổ dueSoon");

        await SeedTaskAsync(scenario, board, todo, me, "Trong ngày", dueDate: Now.AddHours(3));
        await SeedTaskAsync(scenario, board, todo, me, "Đúng mép trên", dueDate: Now.AddDays(3));
        await SeedTaskAsync(scenario, board, todo, me, "Vượt mép trên 1 giây", dueDate: Now.AddDays(3).AddSeconds(1));
        await SeedTaskAsync(scenario, board, todo, me, "Quá hạn", dueDate: Now.AddSeconds(-1));

        using var client = await scenario.AsUserAsync(me);
        var dashboard = await client.GetJsonAsync<DashboardDto>($"/api/workspaces/{workspace.Id}/dashboard");

        Assert.NotNull(dashboard);
        Assert.Equal(3, dashboard!.DueSoonDays);
        Assert.Equal(2, dashboard.MyTasks.DueSoon.Count);
        Assert.Equal("Trong ngày", dashboard.MyTasks.DueSoon.Items[0].Title);
        Assert.Equal("Đúng mép trên", dashboard.MyTasks.DueSoon.Items[1].Title);

        // The overdue task is NOT repeated in dueSoon: the tabs are an urgency partition.
        Assert.Equal(1, dashboard.MyTasks.Overdue.Count);
    }

    [Fact]
    public async Task DueSoon_DaysParameter_MovesTheUpperEdge_AndClampsOutOfRangeValues()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var (me, workspace, board, todo, _) = await SeedUserWithBoardAsync(scenario, "Tham số days");

        await SeedTaskAsync(scenario, board, todo, me, "Mười ngày nữa", dueDate: Now.AddDays(10));

        using var client = await scenario.AsUserAsync(me);

        var three = await client.GetJsonAsync<DashboardDto>($"/api/workspaces/{workspace.Id}/dashboard");
        Assert.Equal(0, three!.MyTasks.DueSoon.Count);
        Assert.Equal(3, three.DueSoonDays);

        var ten = await client.GetJsonAsync<DashboardDto>($"/api/workspaces/{workspace.Id}/dashboard?days=10");
        Assert.Equal(1, ten!.MyTasks.DueSoon.Count);
        Assert.Equal(10, ten.DueSoonDays);

        // Out of range clamps instead of failing (days/take are UI knobs, not client input worth a 400).
        var zero = await client.GetJsonAsync<DashboardDto>($"/api/workspaces/{workspace.Id}/dashboard?days=0");
        Assert.Equal(1, zero!.DueSoonDays);

        var huge = await client.GetJsonAsync<DashboardDto>($"/api/workspaces/{workspace.Id}/dashboard?days=99");
        Assert.Equal(30, huge!.DueSoonDays);

        // A value the binder cannot parse at all never reaches the service.
        var invalid = await client.GetAsync($"/api/workspaces/{workspace.Id}/dashboard?days=abc");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task RecentlyAssigned_HonoursTheSevenDayWindow_AndOnlyMyTasks()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var (me, workspace, board, todo, done) = await SeedUserWithBoardAsync(scenario, "Mới giao");
        var other = await scenario.CreateUserAsync("Người khác", "dash.other@example.test");
        await AddMemberAsync(scenario, workspace.Id, other, WorkspaceRole.Member);

        await SeedTaskAsync(scenario, board, todo, me, "Vừa giao", createdAt: Now.AddHours(-2));
        await SeedTaskAsync(scenario, board, todo, me, "Sáu ngày trước", createdAt: Now.AddDays(-6));
        await SeedTaskAsync(scenario, board, todo, me, "Tám ngày trước", createdAt: Now.AddDays(-8));
        // A finished task never appears in "mới giao": it is no longer something to pick up. Put it in
        // the DONE column — seeding a completed_at into a Todo column is an inconsistent row the app
        // itself cannot produce (completed_at is stamped by moving into an is_done column).
        await SeedTaskAsync(scenario, board, done, me, "Xong rồi", createdAt: Now.AddHours(-1));
        await SeedTaskAsync(scenario, board, todo, other, "Của người khác", createdAt: Now.AddHours(-1));

        using var client = await scenario.AsUserAsync(me);
        var dashboard = await client.GetJsonAsync<DashboardDto>($"/api/workspaces/{workspace.Id}/dashboard");

        Assert.NotNull(dashboard);
        Assert.Equal(2, dashboard!.MyTasks.RecentlyAssigned.Count);

        // Newest first.
        Assert.Equal("Vừa giao", dashboard.MyTasks.RecentlyAssigned.Items[0].Title);
        Assert.Equal("Sáu ngày trước", dashboard.MyTasks.RecentlyAssigned.Items[1].Title);
    }

    [Fact]
    public async Task BucketCount_IsTheRealTotal_NotTheTruncatedItemCount()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var (me, workspace, board, todo, _) = await SeedUserWithBoardAsync(scenario, "Cắt trang");

        for (var i = 0; i < 15; i++)
        {
            await SeedTaskAsync(scenario, board, todo, me, $"Quá hạn {i:D2}", dueDate: Now.AddDays(-1).AddMinutes(-i));
        }

        using var client = await scenario.AsUserAsync(me);
        var dashboard = await client.GetJsonAsync<DashboardDto>(
            $"/api/workspaces/{workspace.Id}/dashboard?take=5");

        Assert.NotNull(dashboard);
        Assert.Equal(15, dashboard!.MyTasks.Overdue.Count);
        Assert.Equal(5, dashboard.MyTasks.Overdue.Items.Count);
    }

    // ---- scope & isolation -------------------------------------------------

    [Fact]
    public async Task TasksOnASoftDeletedBoard_AreHidden()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var (me, workspace, board, todo, _) = await SeedUserWithBoardAsync(scenario, "Board bị xoá");

        await SeedTaskAsync(scenario, board, todo, me, "Của board sống", dueDate: Now.AddDays(-1));

        var (deadBoard, deadTodo, _) = await AddBoardAsync(scenario, workspace.Id, "Board đã xoá");
        await SeedTaskAsync(scenario, deadBoard, deadTodo, me, "Của board đã xoá", dueDate: Now.AddDays(-1));

        await using (var db = scenario.NewDbContext())
        {
            var row = await db.Boards.FirstAsync(b => b.Id == deadBoard.Id);
            row.DeletedAt = Now;
            await db.SaveChangesAsync();
        }

        using var client = await scenario.AsUserAsync(me);
        var dashboard = await client.GetJsonAsync<DashboardDto>($"/api/workspaces/{workspace.Id}/dashboard");

        Assert.NotNull(dashboard);
        Assert.Equal(1, dashboard!.MyTasks.Overdue.Count);
        Assert.Equal("Của board sống", dashboard.MyTasks.Overdue.Items[0].Title);
        Assert.Single(dashboard.Boards);
    }

    [Fact]
    public async Task TasksOfAnotherWorkspace_NeverAppear()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var (me, workspace, board, todo, _) = await SeedUserWithBoardAsync(scenario, "Workspace A");
        await SeedTaskAsync(scenario, board, todo, me, "Việc của A", dueDate: Now.AddDays(-1));

        // The same user, a second workspace, an overdue task assigned to them.
        var second = await scenario.CreateWorkspaceAsync(me, "Workspace B");
        var (otherBoard, otherTodo, _) = await AddBoardAsync(scenario, second.Id, "Board B");
        await SeedTaskAsync(scenario, otherBoard, otherTodo, me, "Việc của B", dueDate: Now.AddDays(-1));

        using var client = await scenario.AsUserAsync(me);
        var dashboard = await client.GetJsonAsync<DashboardDto>($"/api/workspaces/{workspace.Id}/dashboard");

        Assert.NotNull(dashboard);
        Assert.Equal(1, dashboard!.MyTasks.Overdue.Count);
        Assert.Equal("Việc của A", dashboard.MyTasks.Overdue.Items[0].Title);
    }

    [Fact]
    public async Task AnOutsider_GetsNotFound()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var (_, workspace, _, _, _) = await SeedUserWithBoardAsync(scenario, "Workspace riêng");
        var outsider = await scenario.CreateUserAsync("Người ngoài", "dash.outsider@example.test");

        using var client = await scenario.AsUserAsync(outsider);
        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}/dashboard");

        // 404, not 403: an outsider must not learn that the workspace exists.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task APlainMember_SeesTheDashboardIncludingRecentActivity()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var (member, workspace, board, todo, _) = await SeedUserWithBoardAsync(scenario, "Member xem dashboard");

        using var client = await scenario.AsUserAsync(member);
        var created = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/tasks",
            new { title = "Việc do member tạo", columnId = todo.Id });

        created.EnsureSuccessStatusCode();

        var dashboard = await client.GetJsonAsync<DashboardDto>($"/api/workspaces/{workspace.Id}/dashboard");

        Assert.NotNull(dashboard);

        // The full activity log is Manager-only (Phase 10 §2.4); this overview is not, and a recent
        // feed the member cannot see would contradict "hoạt động gần đây" on their own home page.
        Assert.NotEmpty(dashboard!.RecentActivities);
        Assert.Contains(
            dashboard.RecentActivities,
            a => a.Action == "TaskCreated" && a.AuthorName == member.DisplayName);
    }

    // ---- totals, summaries, truncation ------------------------------------

    [Fact]
    public async Task SummaryAndBoardBreakdown_AddUpToTheSameNumbers()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var (me, workspace, board, todo, done) = await SeedUserWithBoardAsync(scenario, "Tổng hợp");

        await SeedTaskAsync(scenario, board, todo, me, "Mở 1", dueDate: Now.AddDays(-1));
        await SeedTaskAsync(scenario, board, todo, me, "Mở 2");
        // Completed but still parked in Todo: this is what the app produces between the move and the
        // column change, and it is the case that proves isDone is "column OR completed_at".
        await SeedTaskAsync(scenario, board, todo, me, "Xong nhờ completedAt", completedAt: Now.AddHours(-1));

        using var client = await scenario.AsUserAsync(me);
        var dashboard = await client.GetJsonAsync<DashboardDto>($"/api/workspaces/{workspace.Id}/dashboard");

        Assert.NotNull(dashboard);
        Assert.Equal(3, dashboard!.Summary.TotalTasks);
        Assert.Equal(1, dashboard.Summary.DoneTasks);
        Assert.Equal(2, dashboard.Summary.OpenTasks);
        Assert.Equal(1, dashboard.Summary.OverdueTasks);
        Assert.Equal(2, dashboard.Summary.MyOpenTasks);

        var summary = Assert.Single(dashboard.Boards);
        Assert.Equal(3, summary.Total);
        Assert.Equal(1, summary.Done);
        Assert.Equal(2, summary.Open);
        Assert.Equal(1, summary.Overdue);

        // Column counts are the "số task theo trạng thái" breakdown and must add up to the total.
        // Note this counts by COLUMN, not by completion: the third task has completed_at set but still
        // sits in Todo (see below), so Todo legitimately holds 3 rows.
        Assert.Equal(summary.Total, summary.Columns.Sum(c => c.Count));
        Assert.Equal(3, summary.Columns.Single(c => c.ColumnId == todo.Id).Count);
        Assert.Equal(0, summary.Columns.Single(c => c.ColumnId == done.Id).Count);
        Assert.True(summary.Columns.Single(c => c.ColumnId == done.Id).IsDone);
    }

    [Fact]
    public async Task MoreThanTwentyBoards_IsTruncatedWithAFlag()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var (me, workspace, _, _, _) = await SeedUserWithBoardAsync(scenario, "Nhiều board");

        for (var i = 0; i < 20; i++)
        {
            await AddBoardAsync(scenario, workspace.Id, $"Board phụ {i:D2}");
        }

        using var client = await scenario.AsUserAsync(me);
        var dashboard = await client.GetJsonAsync<DashboardDto>($"/api/workspaces/{workspace.Id}/dashboard");

        Assert.NotNull(dashboard);
        Assert.True(dashboard!.BoardsTruncated);
        Assert.Equal(20, dashboard.Boards.Count);
    }

    [Fact]
    public async Task RecentActivities_CarryNoPayload_AndStopAtTen()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var (me, workspace, board, todo, _) = await SeedUserWithBoardAsync(scenario, "Feed hoạt động");

        using var client = await scenario.AsUserAsync(me);
        for (var i = 0; i < 12; i++)
        {
            var created = await client.PostJsonAsync(
                $"/api/boards/{board.Id}/tasks",
                new { title = $"Thẻ {i:D2}", columnId = todo.Id });
            created.EnsureSuccessStatusCode();
        }

        var dashboard = await client.GetJsonAsync<DashboardDto>($"/api/workspaces/{workspace.Id}/dashboard");

        Assert.NotNull(dashboard);
        Assert.Equal(10, dashboard!.RecentActivities.Count);

        // Newest first, and the raw jsonb diff is never widened to every member (decision P5).
        Assert.All(dashboard.RecentActivities, a => Assert.Null(a.Payload));
        Assert.Equal(
            dashboard.RecentActivities.OrderByDescending(a => a.CreatedAt).Select(a => a.Id),
            dashboard.RecentActivities.Select(a => a.Id));
    }

    // ---- Phase 14 §3.2: project health ------------------------------------

    [Fact]
    public async Task HEALTH_DASH_1_WorkspaceWithOverdueWork_ScoresBelowFullMarksAndSaysWhy()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var (me, workspace, board, todo, _) = await SeedUserWithBoardAsync(scenario, "Sức khỏe có vấn đề");

        // Ba người khác nhau giữ ba thẻ ⇒ khoản `load` = 0, và mọi thẻ tạo ở `Now` ⇒ khoản `aging` = 0.
        // Nhờ vậy con số khẳng định dưới đây chỉ nói về ĐÚNG khoản `overdue`, không bị pha tạp.
        var second = await scenario.CreateUserAsync("Người thứ hai");
        var third = await scenario.CreateUserAsync("Người thứ ba");
        await AddMemberAsync(scenario, workspace.Id, second, WorkspaceRole.Member);
        await AddMemberAsync(scenario, workspace.Id, third, WorkspaceRole.Member);

        await SeedTaskAsync(scenario, board, todo, me, "Trễ 3 ngày", dueDate: Now.AddDays(-3), createdAt: Now);
        await SeedTaskAsync(scenario, board, todo, second, "Trễ 1 ngày", dueDate: Now.AddDays(-1), createdAt: Now);
        await SeedTaskAsync(scenario, board, todo, third, "Còn hạn", dueDate: Now.AddDays(10), createdAt: Now);

        using var client = await scenario.AsUserAsync(me);
        var dashboard = await client.GetJsonAsync<DashboardDto>($"/api/workspaces/{workspace.Id}/dashboard");

        Assert.NotNull(dashboard);
        var health = dashboard!.Health;

        Assert.NotNull(health);
        Assert.True(health!.Score < 100, $"Điểm phải dưới 100 khi có 2/3 thẻ quá hạn (nhận {health.Score}).");
        Assert.True(health.Components["overdue"] > 0);

        // 2/3 quá hạn ⇒ 40 × 2/3 ≈ 26.67 ⇒ điểm 73. Bốn khoản còn lại phải là 0 (xem lý do ở trên).
        Assert.Equal(26.67, health.Components["overdue"]);
        Assert.Equal(0, health.Components["atRisk"]);
        Assert.Equal(0, health.Components["stalled"]);
        Assert.Equal(0, health.Components["aging"]);
        Assert.Equal(0, health.Components["load"]);
        Assert.Equal(73, health.Score);
        Assert.Equal("Cần chú ý", health.Band);

        // Lý do phải nêu CON SỐ thật để người đọc hiểu ngay, không phải câu chung chung.
        Assert.Contains(health.Reasons, r => r.Contains("2 thẻ quá hạn", StringComparison.Ordinal));
        Assert.Single(health.Reasons);
    }

    [Fact]
    public async Task HEALTH_DASH_2_EmptyWorkspace_ScoresFullMarks()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var (me, workspace, _, _, _) = await SeedUserWithBoardAsync(scenario, "Sức khỏe rỗng");

        // Board rỗng (không thẻ nào): "không có việc đang mở ⇒ không có gì để chậm".
        using var client = await scenario.AsUserAsync(me);
        var dashboard = await client.GetJsonAsync<DashboardDto>($"/api/workspaces/{workspace.Id}/dashboard");

        Assert.NotNull(dashboard);
        Assert.NotNull(dashboard!.Health);
        Assert.Equal(100, dashboard.Health!.Score);
        Assert.Equal("Tốt", dashboard.Health.Band);
        Assert.All(dashboard.Health.Components.Values, value => Assert.Equal(0, value));
        Assert.Empty(dashboard.Health.Reasons);
    }

    [Fact]
    public async Task HEALTH_DASH_3_Regression_TheNineOriginalFieldsKeepTheirNamesAndTypes()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var (me, workspace, board, todo, _) = await SeedUserWithBoardAsync(scenario, "Hồi quy hình dạng");

        await SeedTaskAsync(scenario, board, todo, me, "Thẻ bình thường", dueDate: Now.AddDays(2));

        using var client = await scenario.AsUserAsync(me);
        var json = await client.Http.GetStringAsync($"/api/workspaces/{workspace.Id}/dashboard");

        // (a) Shape của 9 field CŨ không đổi: một client viết TRƯỚC Giai đoạn 14 vẫn đọc được nguyên vẹn.
        //     Đây chính là bằng chứng cho quyết định D15 ("append, không sửa").
        var legacy = System.Text.Json.JsonSerializer.Deserialize<LegacyDashboardDto>(
            json,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.NotNull(legacy);
        Assert.Equal(workspace.Id, legacy!.WorkspaceId);
        Assert.Equal("Hồi quy hình dạng", legacy.WorkspaceName);
        Assert.Equal(3, legacy.DueSoonDays);
        Assert.NotNull(legacy.MyTasks);
        Assert.Single(legacy.Boards);
        Assert.False(legacy.BoardsTruncated);
        Assert.NotNull(legacy.RecentActivities);
        Assert.Equal(1, legacy.Summary.OpenTasks);

        // (b) `health` là field MỚI, có mặt trong payload thật, và là field CUỐI của object.
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("health", out var healthElement));
        Assert.Equal(System.Text.Json.JsonValueKind.Object, healthElement.ValueKind);
        Assert.Equal("health", root.EnumerateObject().Last().Name);
    }

    [Fact]
    public async Task HEALTH_DASH_4_HealthAgreesWithTheSummaryOnTheSameResponse()
    {
        await using var scenario = await _database.CreateClockScenarioAsync(new FixedTimeProvider(Now));
        var (me, workspace, board, todo, _) = await SeedUserWithBoardAsync(scenario, "Đối chiếu KPI");

        // Ba người giữ ba thẻ ⇒ `load` = 0 (không ai giữ từ 2 thẻ trở lên).
        var second = await scenario.CreateUserAsync("Người thứ hai");
        var third = await scenario.CreateUserAsync("Người thứ ba");
        await AddMemberAsync(scenario, workspace.Id, second, WorkspaceRole.Member);
        await AddMemberAsync(scenario, workspace.Id, third, WorkspaceRole.Member);

        await SeedTaskAsync(scenario, board, todo, me, "Quá hạn", dueDate: Now.AddDays(-1), createdAt: Now);
        await SeedTaskAsync(scenario, board, todo, second, "Sắp tới hạn", dueDate: Now.AddDays(1), createdAt: Now);
        await SeedTaskAsync(scenario, board, todo, third, "Không hạn", dueDate: null, createdAt: Now);

        using var client = await scenario.AsUserAsync(me);
        var dashboard = await client.GetJsonAsync<DashboardDto>($"/api/workspaces/{workspace.Id}/dashboard");

        Assert.NotNull(dashboard);
        var summary = dashboard!.Summary;
        var health = dashboard.Health;

        Assert.NotNull(health);

        // 1/3 thẻ mở quá hạn ⇒ khoản trừ quá hạn phải là 40 × 1/3 ≈ 13.33, tức là **cùng một đại lượng**
        // mà thẻ KPI "Thẻ quá hạn" đang hiển thị. Gauge và 5 KPI cùng đọc một nguồn ⇒ không thể lệch.
        Assert.Equal(3, summary.OpenTasks);
        Assert.Equal(1, summary.OverdueTasks);
        Assert.Equal(13.33, health!.Components["overdue"]);
        Assert.Equal(0, health.Components["aging"]);
        Assert.Equal(0, health.Components["load"]);
        Assert.Equal(87, health.Score);
        Assert.Equal("Tốt", health.Band);

        // 5 khoá components luôn hiện diện, kể cả khi phần lớn bằng 0.
        Assert.Equal(5, health.Components.Count);
        Assert.Contains("overdue", health.Components.Keys);
        Assert.Contains("atRisk", health.Components.Keys);
        Assert.Contains("stalled", health.Components.Keys);
        Assert.Contains("aging", health.Components.Keys);
        Assert.Contains("load", health.Components.Keys);

        // Thẻ không có hạn KHÔNG được tính là "sắp hết hạn" (không bịa hạn cho thẻ không có hạn).
        Assert.Equal(0, health.Components["atRisk"]);

        // Và lý do chỉ nêu đúng khoản đang bị trừ.
        Assert.Single(health.Reasons);
        Assert.Contains(health.Reasons, r => r.Contains("1 thẻ quá hạn", StringComparison.Ordinal));
    }

    // ---- seeding helpers ---------------------------------------------------

    /// <summary>
    /// Creates <paramref name="workspaceName"/>'s user + workspace + one board with a Todo and a Done
    /// column, owned by that user, and returns the ids the tests need.
    /// </summary>
    private static async Task<(ApplicationUser User, Workspace Workspace, Board Board, BoardColumn Todo, BoardColumn Done)>
        SeedUserWithBoardAsync(TestScenario scenario, string workspaceName)
    {
        var user = await scenario.CreateUserAsync(workspaceName);
        var workspace = await scenario.CreateWorkspaceAsync(user, workspaceName);
        var (board, todo, done) = await scenario.CreateBoardAsync(workspace, "Board chính");
        return (user, workspace, board, todo, done);
    }

    /// <summary>
    /// Adds a board with a Todo/Done pair to an existing workspace. Needed because
    /// <c>TestScenario.CreateBoardAsync</c> always makes a brand-new board — truncation and
    /// soft-delete tests need several on one workspace.
    /// </summary>
    private static async Task<(Board Board, BoardColumn Todo, BoardColumn Done)> AddBoardAsync(
        TestScenario scenario, Guid workspaceId, string name)
    {
        await using var db = scenario.NewDbContext();

        var board = new Board { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Name = name };
        var todo = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Name = "Todo",
            Position = 0,
            IsDone = false,
        };
        var done = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Name = "Done",
            Position = 1,
            IsDone = true,
        };

        db.Boards.Add(board);
        db.BoardColumns.AddRange(todo, done);
        await db.SaveChangesAsync();

        return (board, todo, done);
    }

    private static async Task AddMemberAsync(
        TestScenario scenario, Guid workspaceId, ApplicationUser user, WorkspaceRole role)
    {
        await using var db = scenario.NewDbContext();
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspaceId,
            UserId = user.Id,
            Role = role,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Inserts a task with full control over the timestamps no endpoint can set.
    /// <para>
    /// <b>Why the post-insert UPDATE:</b> <c>TeamNexusDbContext.SaveChanges</c> stamps
    /// <c>created_at</c>/<c>updated_at</c> with the real wall clock for every added
    /// <c>IAuditableEntity</c> (it only stamps on <c>Added</c>/<c>Modified</c>, never restores what the
    /// caller set). A dashboard test that wants a task from six days ago therefore cannot express that
    /// in the INSERT — the row would always land at "now" and every window test would pass for the
    /// wrong reason. Writing the column right after the insert is the smallest honest fix, and it is
    /// exactly the kind of fixture-only SQL the suite owns.
    /// </para>
    /// <para>
    /// <c>completed_at</c> needs no such treatment: the stamping only touches the two audit columns.
    /// </para>
    /// </summary>
    private static async Task<BoardTask> SeedTaskAsync(
        TestScenario scenario,
        Board board,
        BoardColumn column,
        ApplicationUser assignee,
        string title,
        DateTimeOffset? dueDate = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? completedAt = null)
    {
        await using var db = scenario.NewDbContext();

        var createdAtValue = createdAt ?? Now.AddMinutes(-30);

        var task = new BoardTask
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            ColumnId = column.Id,
            Title = title,
            Position = 0,
            CreatedBy = assignee.Id,
            AssigneeId = assignee.Id,
            DueDate = dueDate,
            CompletedAt = completedAt,
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        // See the remark above: the audit stamping overwrote what we passed in, so set it explicitly.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE tasks SET created_at = {0}, updated_at = {0} WHERE id = {1}",
            createdAtValue,
            task.Id);

        task.CreatedAt = createdAtValue;
        task.UpdatedAt = createdAtValue;
        return task;
    }

    // ---- response shapes ---------------------------------------------------

    /// <summary>
    /// The payload as it looked <b>before</b> Phase 14 — nine positional fields and <b>no</b> reference
    /// to <c>health</c> at all. Used by <c>HEALTH_DASH_3</c> to prove the additive change really is
    /// additive: an older client keeps deserializing the same nine names and types, and simply never
    /// sees the new one.
    /// </summary>
    private sealed record LegacyDashboardDto(
        Guid WorkspaceId,
        string WorkspaceName,
        DateTimeOffset UtcNow,
        int DueSoonDays,
        MyTasksDto MyTasks,
        IReadOnlyList<BoardSummaryDto> Boards,
        bool BoardsTruncated,
        IReadOnlyList<ActivityItemDto> RecentActivities,
        SummaryDto Summary);

    private sealed record DashboardDto(
        Guid WorkspaceId,
        string WorkspaceName,
        DateTimeOffset UtcNow,
        int DueSoonDays,
        MyTasksDto MyTasks,
        IReadOnlyList<BoardSummaryDto> Boards,
        bool BoardsTruncated,
        IReadOnlyList<ActivityItemDto> RecentActivities,
        SummaryDto Summary)
    {
        /// <summary>
        /// Phase 14 §3.2 — appended as a property rather than a positional parameter, exactly like the
        /// production record, so every pre-Phase-14 construction of this test DTO keeps compiling.
        /// </summary>
        public ProjectHealthDto? Health { get; init; }
    }

    private sealed record ProjectHealthDto(
        int Score,
        string Band,
        Dictionary<string, double> Components,
        List<string> Reasons);

    private sealed record MyTasksDto(BucketDto Overdue, BucketDto DueSoon, BucketDto RecentlyAssigned);

    private sealed record BucketDto(int Count, IReadOnlyList<DashboardTaskDto> Items);

    private sealed record DashboardTaskDto(
        Guid Id,
        Guid BoardId,
        Guid ColumnId,
        string Title,
        string BoardName,
        string ColumnName,
        DateTimeOffset? DueDate,
        string? Priority,
        DateTimeOffset CreatedAt,
        Guid? AssigneeId,
        bool IsDone,
        int? OverdueByDays);

    private sealed record BoardSummaryDto(
        Guid BoardId,
        string Name,
        int Total,
        int Done,
        int Open,
        int Overdue,
        IReadOnlyList<ColumnCountDto> Columns);

    private sealed record ColumnCountDto(Guid ColumnId, string Name, bool IsDone, int Count);

    private sealed record ActivityItemDto(
        Guid Id,
        Guid? BoardId,
        string EntityType,
        string Action,
        string? AuthorName,
        DateTimeOffset CreatedAt,
        System.Text.Json.JsonElement? Payload);

    private sealed record SummaryDto(
        int TotalTasks,
        int DoneTasks,
        int OpenTasks,
        int OverdueTasks,
        int MyOpenTasks);
}
