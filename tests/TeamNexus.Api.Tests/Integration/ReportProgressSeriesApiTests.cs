using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Persistence.Data.Entities;
using TeamNexus.Modules.Board.Services;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 13 §2 — <c>GET /api/workspaces/{workspaceId}/reports/progress-series</c> (PSA-1…PSA-8).
/// <para>
/// The pure suite (<c>Pure/ReportProgressSeriesTests</c>) owns the arithmetic; this one owns the contract:
/// who may call it, what the status codes are for bad input, and that the numbers actually reach the wire.
/// Tasks are seeded directly because the suite needs control over <c>created_at</c>, which no endpoint can
/// set (the same reason, and the same fixture SQL, as <c>DashboardApiTests</c>).
/// </para>
/// </summary>
public sealed class ReportProgressSeriesApiTests : IClassFixture<DatabaseFixture>
{
    /// <summary>Window used by the seeded cases. Absolute, so a wall-clock leak is obvious.</summary>
    private static readonly DateTimeOffset From = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset To = new(2026, 3, 7, 0, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture _database;

    public ReportProgressSeriesApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- PSA-1 -----------------------------------------------------------------

    [Fact]
    public async Task PSa1_SevenDayWindow_ReturnsSevenDailyBucketsMatchingTheSeed()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        // Created inside the window and closed inside it.
        await SeedTaskAsync(scenario, seed.BoardOrThrow.Id, seed.TodoOrThrow.Id, seed.User.Id, "Tạo & xong trong kỳ",
            createdAt: From.AddDays(1).AddHours(9), completedAt: From.AddDays(3).AddHours(15));

        // Created inside the window, still open.
        await SeedTaskAsync(scenario, seed.BoardOrThrow.Id, seed.TodoOrThrow.Id, seed.User.Id, "Còn mở",
            createdAt: From.AddDays(2).AddHours(9));

        // A backlog task created before the window: it must appear in openTasks from bucket 0.
        await SeedTaskAsync(scenario, seed.BoardOrThrow.Id, seed.TodoOrThrow.Id, seed.User.Id, "Tồn từ trước",
            createdAt: From.AddDays(-5));

        using var client = await scenario.AsUserAsync(seed.User);
        var series = await client.GetJsonAsync<SeriesDto>(Url(seed.Workspace.Id, From, To));

        Assert.NotNull(series);
        Assert.Equal("date", series!.Mode);
        Assert.Equal(1, series.BucketDays);
        Assert.Equal(7, series.Days.Count);
        Assert.Empty(series.Weeks);

        // Bucket 0 = 2026-03-01: only the backlog task is open, nothing created yet.
        Assert.Equal(new DateOnly(2026, 3, 1), series.Days[0].Date);
        Assert.Equal(1, series.Days[0].OpenTasks);
        Assert.Equal(0, series.Days[0].Creations);

        // 2026-03-02: the first task is created ⇒ 2 open at day end (backlog + it).
        Assert.Equal(2, series.Days[1].OpenTasks);
        Assert.Equal(1, series.Days[1].Creations);

        // 2026-03-04: it is completed ⇒ 1 completion; 2 open (backlog + "Còn mở", created 03-03).
        Assert.Equal(new DateOnly(2026, 3, 4), series.Days[3].Date);
        Assert.Equal(1, series.Days[3].Completions);
        Assert.Equal(2, series.Days[3].OpenTasks);

        Assert.Equal(1, series.Velocity.CompletedInRange);
        Assert.Equal(2, series.Velocity.OpenAtEnd);
        Assert.Equal(0, series.TzOffsetMinutes);
        Assert.False(series.Truncated.BucketCapReached);
    }

    // ---- PSA-2 -----------------------------------------------------------------

    [Fact]
    public async Task PSa2_BoardScopeFiltersTheNumbers_AndAForeignBoardIsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        var (otherBoard, otherTodo, _) = await scenario.CreateBoardAsync(seed.Workspace, "Board phụ");

        await SeedTaskAsync(scenario, seed.BoardOrThrow.Id, seed.TodoOrThrow.Id, seed.User.Id, "Thuộc board chính",
            createdAt: From.AddDays(1));
        await SeedTaskAsync(scenario, otherBoard.Id, otherTodo.Id, seed.User.Id, "Thuộc board phụ",
            createdAt: From.AddDays(1));

        using var client = await scenario.AsUserAsync(seed.User);

        var scoped = await client.GetJsonAsync<SeriesDto>(Url(seed.Workspace.Id, From, To, boardId: seed.BoardOrThrow.Id));
        Assert.NotNull(scoped);
        Assert.Equal("board", scoped!.Scope.Type);
        Assert.Equal(seed.BoardOrThrow.Id, scoped.Scope.BoardId);
        Assert.Equal(1, scoped.Velocity.OpenAtEnd);

        // A board from another workspace is 404 — the same rule as /summary (do not leak existence).
        var stranger = await scenario.CreateUserAsync("Người lạ");
        var foreignWorkspace = await scenario.CreateWorkspaceAsync(stranger, "Workspace khác");
        var (foreignBoard, _, _) = await scenario.CreateBoardAsync(foreignWorkspace, "Board lạ");

        var notFound = await client.GetAsync(Url(seed.Workspace.Id, From, To, boardId: foreignBoard.Id));
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    // ---- PSA-3 -----------------------------------------------------------------

    [Fact]
    public async Task PSa3_MemberIsForbidden_OutsiderIsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        var member = await scenario.CreateUserAsync("Thành viên", "member@example.test");
        await AddMemberAsync(scenario, seed.Workspace.Id, member.Id, WorkspaceRole.Member);

        using (var memberClient = await scenario.AsUserAsync(member))
        {
            var forbidden = await memberClient.GetAsync(Url(seed.Workspace.Id, From, To));

            // Member may read /dashboard (Phase 12) but NOT reports — the same gate as /summary.
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        var outsider = await scenario.CreateUserAsync("Người ngoài", "outsider@example.test");
        using var outsiderClient = await scenario.AsUserAsync(outsider);

        var hidden = await outsiderClient.GetAsync(Url(seed.Workspace.Id, From, To));

        // 404, not 403: a stranger must not learn that the workspace exists.
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    // ---- PSA-4 -----------------------------------------------------------------

    [Fact]
    public async Task PSa4_BadParametersAreRejectedButAnOutOfRangeTimezoneIsClamped()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var reversed = await client.GetAsync(
            $"/api/workspaces/{seed.Workspace.Id}/reports/progress-series?from=2026-03-07T00:00:00Z&to=2026-03-01T00:00:00Z");
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);

        var badDate = await client.GetAsync(
            $"/api/workspaces/{seed.Workspace.Id}/reports/progress-series?from=abc");
        Assert.Equal(HttpStatusCode.BadRequest, badDate.StatusCode);

        var badTz = await client.GetAsync(
            $"/api/workspaces/{seed.Workspace.Id}/reports/progress-series?from=2026-03-01T00:00:00Z&to=2026-03-07T00:00:00Z&tzOffsetMinutes=abc");
        Assert.Equal(HttpStatusCode.BadRequest, badTz.StatusCode);

        // Out-of-range is a UI knob, not a contract: clamped and echoed, never a 400.
        var clamped = await client.GetJsonAsync<SeriesDto>(Url(seed.Workspace.Id, From, To, tz: 99999));
        Assert.NotNull(clamped);
        Assert.Equal(840, clamped!.TzOffsetMinutes);
    }

    [Fact]
    public async Task PSa4b_NegativeTimezoneIsAcceptedAndEchoed()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var series = await client.GetJsonAsync<SeriesDto>(Url(seed.Workspace.Id, From, To, tz: -300));

        Assert.NotNull(series);
        Assert.Equal(-300, series!.TzOffsetMinutes);
    }

    // ---- PSA-5 -----------------------------------------------------------------

    [Fact]
    public async Task PSa5_NoExplicitRange_FallsBackToTheConfiguredDefaultWindow()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var series = await client.GetJsonAsync<SeriesDto>(
            $"/api/workspaces/{seed.Workspace.Id}/reports/progress-series");

        Assert.NotNull(series);

        // Reports:DefaultRangeDays = 30 ⇒ the window ends "now" and spans 30 days. Asserting the
        // invariants (not the exact instant) keeps this from being a wall-clock race.
        //
        // `Period.Days` comes from `ReportThresholds.BuildRange`, which counts *elapsed* days with ceil,
        // while the series counts *calendar buckets* inclusive — hence 30 vs 31 for the same window. Both
        // are right for their own job, so this test pins the properties a caller actually depends on.
        Assert.InRange(series!.Period.Days, 30, 31);
        Assert.True(series.Period.To <= DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(30, (series.Period.To - series.Period.From).TotalDays);

        Assert.Equal("date", series.Mode);
        Assert.Equal(DateOnly.FromDateTime(series.Period.From.UtcDateTime), series.Days[0].Date);
        Assert.Equal(DateOnly.FromDateTime(series.Period.To.UtcDateTime), series.Days[^1].Date);
        Assert.Equal(31, series.Days.Count);
    }

    // ---- PSA-6 -----------------------------------------------------------------

    [Fact]
    public async Task PSa6_WhenReportingIsDisabled_Returns503()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        // A dedicated host with Reports:Enabled=false, so the production off-switch is exercised without
        // disturbing the shared host every other suite uses.
        using var disabledFactory = new TeamNexusApiFactory
        {
            ConnectionString = TestScenario.ConnectionStringForTests,
            ReportsEnabled = false,
        };

        using var client = disabledFactory.CreateClient();
        client.BaseAddress = TestScenario.BaseAddress;

        // Bearer, not a cookie: this request is a GET (no CSRF protection) and the only thing under test
        // is the feature gate, so the cheapest authenticating transport is the right one here.
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", TestJwt.Create(seed.User, "Admin"));

        var response = await client.GetAsync(Url(seed.Workspace.Id, From, To));

        // 503 (feature off), not 403: the caller is a workspace Admin, so the permission gate passes and
        // the feature gate is what answers.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // ---- PSA-7 -----------------------------------------------------------------

    [Fact]
    public async Task PSa7_EmptyWorkspace_ReturnsZeroesWithoutNaN()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario, withBoard: false);
        using var client = await scenario.AsUserAsync(seed.User);

        var series = await client.GetJsonAsync<SeriesDto>(Url(seed.Workspace.Id, From, To));

        Assert.NotNull(series);
        Assert.Equal(7, series!.Days.Count);
        Assert.All(series.Days, day =>
        {
            Assert.Equal(0, day.OpenTasks);
            Assert.Equal(0, day.Completions);
            Assert.Equal(0, day.Creations);
        });

        Assert.Equal(0.0, series.Velocity.AvgCompletionsPerWeek);
        Assert.True(double.IsFinite(series.Velocity.AvgCompletionsPerWeek));
        Assert.Equal(0, series.Velocity.OpenAtEnd);
    }

    // ---- PSA-8: regression guards ---------------------------------------------

    [Fact]
    public async Task PSa8_SummaryContractIsUnchangedByTheNewEndpoint()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var summary = await client.GetJsonAsync<SummaryDto>(
            $"/api/workspaces/{seed.Workspace.Id}/reports/summary");

        Assert.NotNull(summary);
        Assert.Equal(seed.Workspace.Id, summary!.WorkspaceId);
        Assert.NotNull(summary.Scope);
        Assert.NotNull(summary.Period);
        Assert.NotNull(summary.Progress);
        Assert.NotNull(summary.Performance);
        Assert.NotNull(summary.ByBoard);
        Assert.NotNull(summary.ByAssignee);
        Assert.NotNull(summary.Activity);
        Assert.NotNull(summary.Health);
        Assert.NotNull(summary.MetricDefinitions);
        Assert.NotNull(summary.Truncated);
    }

    [Fact]
    public async Task PSa8b_SeriesMetricDefinitionsAreReturnedToTheClient()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var series = await client.GetJsonAsync<SeriesDto>(Url(seed.Workspace.Id, From, To));

        Assert.NotNull(series);
        Assert.Contains("openTasks", series!.MetricDefinitions.Keys);
        Assert.Contains("completions", series.MetricDefinitions.Keys);
        Assert.Contains("idealOpenSeries", series.MetricDefinitions.Keys);
    }

    // ---- helpers ---------------------------------------------------------------

    private static string Url(
        Guid workspaceId,
        DateTimeOffset from,
        DateTimeOffset to,
        Guid? boardId = null,
        int? tz = null)
    {
        var query = $"?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";

        if (boardId is { } board)
        {
            query += $"&boardId={board:D}";
        }

        if (tz is { } offset)
        {
            query += $"&tzOffsetMinutes={offset}";
        }

        return $"/api/workspaces/{workspaceId}/reports/progress-series{query}";
    }

    private static async Task<Seed> SeedAsync(TestScenario scenario, bool withBoard = true)
    {
        var user = await scenario.CreateUserAsync("Quản lý");
        var workspace = await scenario.CreateWorkspaceAsync(user, "Workspace báo cáo");

        if (!withBoard)
        {
            // `CreateBoardAsync` always creates one, but PSA-7 needs a workspace with no board at all.
            return new Seed(user, workspace, null, null, null);
        }

        var (board, todo, done) = await scenario.CreateBoardAsync(workspace, "Board chính");
        return new Seed(user, workspace, board, todo, done);
    }

    /// <summary>The workspace under test. <c>Board</c>/<c>Todo</c>/<c>Done</c> are null for the empty case.</summary>
    private sealed record Seed(
        ApplicationUser User,
        Workspace Workspace,
        Board? Board,
        BoardColumn? Todo,
        BoardColumn? Done)
    {
        /// <summary>The seeded board, asserted non-null — every caller here asks for one.</summary>
        public Board BoardOrThrow => Board
            ?? throw new InvalidOperationException("Seed này không tạo board.");

        public BoardColumn TodoOrThrow => Todo
            ?? throw new InvalidOperationException("Seed này không tạo board.");
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

    /// <summary>
    /// Inserts a task with full control over the timestamps no endpoint can set, then writes
    /// <c>created_at</c> straight after the insert.
    /// <para>
    /// <b>Why the post-insert UPDATE:</b> <c>TeamNexusDbContext.SaveChanges</c> stamps
    /// <c>created_at</c>/<c>updated_at</c> with the real wall clock for every added
    /// <see cref="IAuditableEntity"/> (it only stamps on <c>Added</c>/<c>Modified</c> and never restores
    /// what the caller set). Without this, every seeded task would land at "now" and each window
    /// assertion would pass for the wrong reason. Exactly the fixture SQL <c>DashboardApiTests</c> owns.
    /// </para>
    /// <para><c>completed_at</c> needs no such treatment: the stamping only touches the audit columns.</para>
    /// </summary>
    private static async Task SeedTaskAsync(
        TestScenario scenario,
        Guid boardId,
        Guid columnId,
        Guid createdBy,
        string title,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? completedAt = null)
    {
        await using var db = scenario.NewDbContext();

        var created = createdAt ?? From.AddDays(1);

        var task = new BoardTask
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            ColumnId = columnId,
            Title = title,
            Position = 0,
            CreatedBy = createdBy,
            CompletedAt = completedAt,
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE tasks SET created_at = {0}, updated_at = {0} WHERE id = {1}",
            created,
            task.Id);
    }

    // ---- response shapes (mirror the wire contract exactly) --------------------

    private sealed record SeriesDto(
        Guid WorkspaceId,
        ScopeDto Scope,
        PeriodDto Period,
        string Mode,
        int BucketDays,
        IReadOnlyList<DayDto> Days,
        IReadOnlyList<WeekDto> Weeks,
        VelocityDto Velocity,
        IReadOnlyDictionary<string, string> MetricDefinitions,
        TruncationDto Truncated,
        int TzOffsetMinutes);

    private sealed record DayDto(DateOnly Date, int OpenTasks, int Completions, int Creations);

    private sealed record WeekDto(DateOnly WeekStart, int Completions, int Creations, int OpenAtEnd);

    private sealed record VelocityDto(double AvgCompletionsPerWeek, int CompletedInRange, int OpenAtEnd);

    private sealed record ScopeDto(string Type, Guid? BoardId, string? BoardName);

    private sealed record PeriodDto(DateTimeOffset From, DateTimeOffset To, int Days, bool Clamped);

    private sealed record TruncationDto(bool BucketCapReached, int MaxBuckets);

    /// <summary>Only the fields the regression guard needs — the full shape is Phase 6's business.</summary>
    private sealed record SummaryDto(
        Guid WorkspaceId,
        object Scope,
        object Period,
        object Progress,
        object Performance,
        IReadOnlyList<object> ByBoard,
        IReadOnlyList<object> ByAssignee,
        object Activity,
        object Health,
        IReadOnlyDictionary<string, string> MetricDefinitions,
        object Truncated);
}
