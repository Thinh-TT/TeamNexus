using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.Services.Jev;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Giai đoạn 14.7 §A3 (auto-triage) and §A4 (smart assignee dispatching).
/// <para>
/// The offline <c>FakeJevDecisionProvider</c> drives these suites, so every branch is reachable with no
/// network and no token spend. The scenario is selected by the <c>FAKE:JEV_*</c> sentinel in the task title —
/// which is also why these tests always put the sentinel at the START of the title.
/// </para>
/// <para>
/// The assertions that matter most are the negative ones: suggestions must be <b>empty</b> outside the
/// high-confidence branch, and no user id may appear anywhere except in a field typed as an id.
/// </para>
/// </summary>
public sealed class Phase147TriageApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public Phase147TriageApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    private static async Task<(ApplicationUser Admin, Workspace Workspace, Board Board)> SeedAsync(
        TestScenario scenario)
    {
        var admin = await scenario.CreateUserAsync("Quản trị");
        var workspace = await scenario.CreateWorkspaceAsync(admin);
        var (board, _, _) = await scenario.CreateBoardAsync(workspace, "Board Triage");

        return (admin, workspace, board);
    }

    /// <summary>
    /// POSTs one triage request and reads the body back. A local helper rather than `ContinueWith` chains:
    /// `TestHttpClient` has no `PostJsonAsync&lt;TResponse&gt;` overload (its generics are on the request
    /// type), and `.ContinueWith(...).Unwrap()` in a dozen tests would bury the assertions.
    /// </summary>
    private static async Task<TriageDto> TriageAsync(TestHttpClient client, Guid boardId, string title)
    {
        var response = await client.PostJsonAsync(
            $"/api/boards/{boardId}/tasks/triage",
            new { title });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TriageDto>();
        Assert.NotNull(body);
        Assert.True(body!.LatencyMs >= 0);

        return body!;
    }

    // ---- §A3 happy path -----------------------------------------------------------------------

    [Fact]
    public async Task Triage_WithAConfidentAnswer_ReturnsASingleUsableSuggestion()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, board) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/tasks/triage",
            new { title = "Sửa lỗi đăng nhập", description = "Người dùng không đăng nhập được sau deploy." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TriageDto>();

        Assert.NotNull(body);
        Assert.Equal(ConfidenceRouting.DirectAssign, body!.RoutingAdvice);
        Assert.False(body.Degraded);

        // The suggestions must come from the workspace's seeded vocabulary, not from a constant.
        Assert.Contains(body.SuggestedDomain, TaxonomyDefaults.Domains.Select(seed => seed.Key));
        Assert.Contains(body.SuggestedWorkType, TaxonomyDefaults.WorkTypes.Select(seed => seed.Key));
        Assert.Contains(body.SuggestedPriority, JevDecisionService.PriorityLevels);

        // A single suggestion is exactly that: no option list to choose from.
        Assert.Empty(body.Options);
    }

    [Fact]
    public async Task Triage_SendsTheWorkspaceVocabularyAsTheChoiceOptionList()
    {
        // The option list is the whole reason a fixed taxonomy exists: Jev can only answer within what the
        // server offered, so the workspace's own curated keys must be what it sees.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, board) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/taxonomies",
            new
            {
                domains = new[] { new { key = "OnlyDomain", label = "Miền duy nhất" } },
                workTypes = new[] { new { key = "OnlyKind", label = "Loại duy nhất" } },
            });

        var body = await TriageAsync(client, board.Id, "Việc bất kỳ");

        Assert.Equal("OnlyDomain", body.SuggestedDomain);
        Assert.Equal("OnlyKind", body.SuggestedWorkType);
    }

    [Fact]
    public async Task Triage_WithAMediumAnswer_OffersOptionsAndLeavesEverySuggestionEmpty()
    {
        // THE core enforcement of Trụ cột 3. If this ever starts returning a filled-in suggestion, the
        // roadmap's "0.50–0.85 ⇒ let the manager choose" contract is broken.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, board) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var body = await TriageAsync(client, board.Id, "FAKE:JEV_MED — việc cần phân loại");

        Assert.Equal(ConfidenceRouting.OfferChoices, body.RoutingAdvice);
        Assert.Null(body.SuggestedDomain);
        Assert.Null(body.SuggestedWorkType);
        Assert.Null(body.SuggestedPriority);

        // …and the alternatives are offered instead, so the UI has something to render.
        Assert.NotEmpty(body.Options);
    }

    [Fact]
    public async Task Triage_WithALowAnswer_FallsBackToAManualDecision()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, board) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var body = await TriageAsync(client, board.Id, "FAKE:JEV_LOW — việc mơ hồ");

        Assert.Equal(ConfidenceRouting.ManualRequired, body.RoutingAdvice);
        Assert.Null(body.SuggestedDomain);
        Assert.Null(body.SuggestedWorkType);
        Assert.Null(body.SuggestedPriority);
        Assert.Empty(body.Options);
        Assert.True(body.Degraded);
    }

    [Fact]
    public async Task Triage_WhenTheProviderFails_DegradesTo200InsteadOf502()
    {
        // Deliberately different from Smart Setup (which answers 502): triage is optional help, so a dead
        // provider must not break a flow the user can complete by hand.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, board) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/tasks/triage",
            new { title = "FAKE:JEV_ERROR — provider sẽ lỗi" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TriageDto>();
        Assert.NotNull(body);
        Assert.True(body!.Degraded);
        Assert.Null(body.SuggestedDomain);
        Assert.Equal(ConfidenceRouting.ManualRequired, body.RoutingAdvice);
    }

    // ---- §A3 guards ---------------------------------------------------------------------------

    [Fact]
    public async Task Triage_WhenJevIsDisabled_Is503()
    {
        await using var scenario = await _database.CreateScenarioAsync(jevEnabled: false);
        var (admin, _, board) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/tasks/triage",
            new { title = "Việc bất kỳ" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Triage_WithAUserOutsideTheWorkspace_IsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (_, _, board) = await SeedAsync(scenario);
        var outsider = await scenario.CreateUserAsync("Người ngoài");
        using var client = await scenario.AsUserAsync(outsider);

        var response = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/tasks/triage",
            new { title = "Việc bất kỳ" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Triage_WithAnUnknownBoard_IsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PostJsonAsync(
            $"/api/boards/{Guid.NewGuid()}/tasks/triage",
            new { title = "Việc bất kỳ" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Triage_WithABlankTitle_IsBadRequest(string title)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, board) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/tasks/triage",
            new { title });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Triage_WithAnOverlongTitle_IsBadRequest()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, board) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/tasks/triage",
            new { title = new string('a', 201) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Triage_WritesNothingAtAll()
    {
        // §A3's invariant: a triage is a suggestion. Nothing may be created — not a task, not an AI action,
        // not a notification, not an activity row.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, board) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var tasksBefore = await scenario.CountAsync<BoardTask>(t => t.BoardId == board.Id);
        var actionsBefore = await scenario.CountAsync<AiActionLog>();
        var notificationsBefore = await scenario.CountAsync<Notification>();
        var activityBefore = await scenario.CountAsync<ActivityLog>(a => a.WorkspaceId == workspace.Id);

        var response = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/tasks/triage",
            new { title = "Sửa lỗi đăng nhập", description = "Mô tả" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(tasksBefore, await scenario.CountAsync<BoardTask>(t => t.BoardId == board.Id));
        Assert.Equal(actionsBefore, await scenario.CountAsync<AiActionLog>());
        Assert.Equal(notificationsBefore, await scenario.CountAsync<Notification>());
        Assert.Equal(activityBefore, await scenario.CountAsync<ActivityLog>(a => a.WorkspaceId == workspace.Id));
    }

    [Fact]
    public async Task Triage_ResponseNeverLeaksAnIdentifierIntoAProseField()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, board) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/tasks/triage",
            new { title = "Sửa lỗi đăng nhập" });

        var raw = await response.Content.ReadAsStringAsync();

        // The board's, workspace's and every member's id are known to the server; none of them may appear in
        // the suggestion text. This is the structural claim the phase makes, asserted on the wire.
        Assert.DoesNotContain(board.Id.ToString(), raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(workspace.Id.ToString(), raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(admin.Id.ToString(), raw, StringComparison.OrdinalIgnoreCase);
    }

    // ---- DTOs ---------------------------------------------------------------------------------

    private sealed record TriageOptionDto(string Field, string Value, double Probability);

    private sealed record TriageDto(
        string? SuggestedDomain,
        string? SuggestedWorkType,
        string? SuggestedPriority,
        bool NeedsSecurityReview,
        double ConfidenceScore,
        string RoutingAdvice,
        List<TriageOptionDto> Options,
        bool Degraded,
        string Model,
        long LatencyMs = 0);
}
