using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.Services.Jev;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Giai đoạn 14.7 §A4 — smart assignee dispatching.
/// <para>
/// The invariant under test is the anti-leak design: Jev is handed opaque <c>cand-N</c> handles, so the
/// <c>Guid</c> in the response can only have come from our own in-process mapping. The suite therefore checks
/// both that the mapping works (a real member id comes back) and that no id ever appears in a prose field.
/// </para>
/// </summary>
public sealed class Phase147AssigneeApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public Phase147AssigneeApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    private static async Task<(ApplicationUser Admin, ApplicationUser Dev, Workspace Workspace, Board Board, BoardTask Task)>
        SeedAsync(TestScenario scenario)
    {
        var admin = await scenario.CreateUserAsync("Quản trị");
        var dev = await scenario.CreateUserAsync("Nguyễn Văn A");

        var workspace = await scenario.CreateWorkspaceAsync(
            admin, others: [(dev, WorkspaceRole.Member)]);

        var (board, todo, _) = await scenario.CreateBoardAsync(workspace, "Board Điều Phối");
        var task = await scenario.CreateTaskAsync(board, todo, admin, "Sửa lỗi thanh toán");

        // A real skill profile for the candidate, so the dispatching context has something to rank on.
        await using (var db = scenario.NewDbContext())
        {
            var membership = await db.WorkspaceMembers
                .FirstAsync(wm => wm.WorkspaceId == workspace.Id && wm.UserId == dev.Id);

            membership.Skills = ["csharp", "dotnet", "postgresql"];
            membership.PrimaryRole = "Backend Dev";
            await db.SaveChangesAsync();
        }

        return (admin, dev, workspace, board, task);
    }

    private static Task<HttpResponseMessage> PostAsync(TestHttpClient client, Guid boardId, Guid taskId)
        => client.PostJsonAsync(
            $"/api/boards/{boardId}/tasks/{taskId}/assignee-suggestions",
            new { });

    // ---- happy path ---------------------------------------------------------------------------

    [Fact]
    public async Task Suggest_WithAConfidentAnswer_MapsTheOpaqueLabelBackToARealUserId()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, dev, _, board, task) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await PostAsync(client, board.Id, task.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<SuggestionDto>();

        Assert.NotNull(body);
        Assert.Equal(ConfidenceRouting.DirectAssign, body!.RoutingAdvice);
        Assert.False(body.Degraded);

        // The recommendation must be a member of THIS workspace — proof the label mapping resolved.
        Assert.NotNull(body.RecommendedUserId);
        Assert.Contains(body.RecommendedUserId!.Value, new[] { admin.Id, dev.Id });
    }

    [Fact]
    public async Task Suggest_AlwaysCarriesAServerWrittenReasonNextToEachCandidate()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, dev, _, board, task) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var body = await (await PostAsync(client, board.Id, task.Id))
            .Content.ReadFromJsonAsync<SuggestionDto>();

        Assert.NotNull(body);
        Assert.NotEmpty(body!.Candidates);

        var devRow = body.Candidates.FirstOrDefault(candidate => candidate.UserId == dev.Id);
        Assert.NotNull(devRow);

        // The reason is composed by us from the stored profile, so it must reflect it — and it must mention
        // the load figure, which is the part a manager cannot see from the member list alone.
        Assert.NotNull(devRow!.Reason);
        Assert.Contains("Backend Dev", devRow.Reason!);
        Assert.Contains("csharp", devRow.Reason!);
        Assert.Contains("thẻ đang mở", devRow.Reason!);
    }

    [Fact]
    public async Task Suggest_OffersAtMostThreeCandidates()
    {
        // The roadmap's middle band is "Top 2-3"; even a high-confidence answer stays a nudge, not a menu.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, workspace, board, task) = await SeedAsync(scenario);

        for (var index = 0; index < 6; index++)
        {
            var extra = await scenario.CreateUserAsync($"Thành viên {index}");
            await using var db = scenario.NewDbContext();
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspace.Id,
                UserId = extra.Id,
                Role = WorkspaceRole.Member,
            });
            await db.SaveChangesAsync();
        }

        using var client = await scenario.AsUserAsync(admin);

        var body = await (await PostAsync(client, board.Id, task.Id))
            .Content.ReadFromJsonAsync<SuggestionDto>();

        Assert.NotNull(body);
        Assert.True(body!.Candidates.Count <= ConfidenceRouting.MaxOfferedCandidates);
    }

    [Fact]
    public async Task Suggest_WithALowConfidenceAnswer_OffersNobody()
    {
        // The road to a wrong assignment is a low-confidence guess rendered as a recommendation.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, _, board, task) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        // The sentinel has to be in the task TITLE, because that is what the dispatch state carries.
        await using (var db = scenario.NewDbContext())
        {
            var stored = await db.Tasks.FirstAsync(t => t.Id == task.Id);
            stored.Title = "FAKE:JEV_LOW — ai nên làm việc này?";
            await db.SaveChangesAsync();
        }

        var body = await (await PostAsync(client, board.Id, task.Id))
            .Content.ReadFromJsonAsync<SuggestionDto>();

        Assert.NotNull(body);
        Assert.Equal(ConfidenceRouting.ManualRequired, body!.RoutingAdvice);
        Assert.Null(body.RecommendedUserId);
        Assert.Empty(body.Candidates);
    }

    [Fact]
    public async Task Suggest_WhenTheProviderFails_DegradesTo200WithNoRecommendation()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, _, board, task) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await using (var db = scenario.NewDbContext())
        {
            var stored = await db.Tasks.FirstAsync(t => t.Id == task.Id);
            stored.Title = "FAKE:JEV_ERROR — provider sẽ lỗi";
            await db.SaveChangesAsync();
        }

        var response = await PostAsync(client, board.Id, task.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<SuggestionDto>();
        Assert.NotNull(body);
        Assert.True(body!.Degraded);
        Assert.Null(body.RecommendedUserId);
    }

    // ---- anti-leak ----------------------------------------------------------------------------

    [Fact]
    public async Task Suggest_NeverPutsAUserIdIntoAProseField()
    {
        // The structural claim: Jev receives `cand-N`, never a Guid, so an id can only appear in a field
        // typed as one. Asserted on the raw wire text, which catches a leak in ANY field.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, dev, workspace, board, task) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await PostAsync(client, board.Id, task.Id);
        var raw = await response.Content.ReadAsStringAsync();

        // The ids are present as JSON values (recommendedUserId + candidates[].userId)…
        Assert.Contains(dev.Id.ToString(), raw, StringComparison.OrdinalIgnoreCase);

        // …so the check has to be done per prose field, not on the whole body.
        var body = await response.Content.ReadFromJsonAsync<SuggestionDto>();
        Assert.NotNull(body);

        foreach (var candidate in body!.Candidates)
        {
            Assert.DoesNotContain(
                candidate.UserId.ToString(), candidate.Reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                candidate.UserId.ToString(), candidate.DisplayName, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(
            board.Id.ToString(), raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            workspace.Id.ToString(), raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Suggest_DoesNotSendMemberEmailsToTheDecisionProvider()
    {
        // Emails are not needed to rank skill fit, so they must not leave the process. Verifiable here
        // because the candidate reason is composed from the SAME context the provider received.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, dev, _, board, task) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var raw = await (await PostAsync(client, board.Id, task.Id)).Content.ReadAsStringAsync();

        Assert.NotNull(dev.Email);
        Assert.DoesNotContain(dev.Email!, raw, StringComparison.OrdinalIgnoreCase);
    }

    // ---- guards -------------------------------------------------------------------------------

    [Fact]
    public async Task Suggest_WhenJevIsDisabled_Is503()
    {
        await using var scenario = await _database.CreateScenarioAsync(jevEnabled: false);
        var (admin, _, _, board, task) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await PostAsync(client, board.Id, task.Id);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Suggest_WithATaskFromAnotherBoard_IsBadRequest()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, workspace, board, task) = await SeedAsync(scenario);
        var (otherBoard, _, _) = await scenario.CreateBoardAsync(workspace, "Board Khác");
        using var client = await scenario.AsUserAsync(admin);

        // The task exists and the caller is a member — the guard is the board match, not visibility.
        var response = await PostAsync(client, otherBoard.Id, task.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Suggest_WithAnUnknownTask_IsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, _, board, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await PostAsync(client, board.Id, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Suggest_WithAUserOutsideTheWorkspace_IsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (_, _, _, board, task) = await SeedAsync(scenario);
        var outsider = await scenario.CreateUserAsync("Người ngoài");
        using var client = await scenario.AsUserAsync(outsider);

        var response = await PostAsync(client, board.Id, task.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Suggest_WritesNothingAtAll()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, workspace, board, task) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var actionsBefore = await scenario.CountAsync<AiActionLog>();
        var notificationsBefore = await scenario.CountAsync<Notification>();
        var activityBefore = await scenario.CountAsync<ActivityLog>(a => a.WorkspaceId == workspace.Id);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, board.Id, task.Id)).StatusCode);

        Assert.Equal(actionsBefore, await scenario.CountAsync<AiActionLog>());
        Assert.Equal(notificationsBefore, await scenario.CountAsync<Notification>());
        Assert.Equal(activityBefore, await scenario.CountAsync<ActivityLog>(a => a.WorkspaceId == workspace.Id));
    }

    [Fact]
    public async Task Suggest_IncludesTheAiAgentAsACandidate()
    {
        // Dispatching work to the workspace's agent is the point of Phase 7, so the agent must be offered.
        // It is found through member_type, never through its display name.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, workspace, board, task) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        // Reading the member list is what materialises the agent lazily.
        await client.GetAsync($"/api/workspaces/{workspace.Id}/members");

        var body = await (await PostAsync(client, board.Id, task.Id))
            .Content.ReadFromJsonAsync<SuggestionDto>();

        Assert.NotNull(body);

        await using var db = scenario.NewDbContext();
        var agentId = await db.WorkspaceMembers
            .Where(wm => wm.WorkspaceId == workspace.Id && wm.MemberType == MemberType.AiAgent)
            .Select(wm => wm.UserId)
            .FirstAsync();

        Assert.Contains(body!.Candidates, candidate => candidate.UserId == agentId);
    }

    private sealed record CandidateDto(Guid UserId, string DisplayName, double Probability, string? Reason);

    private sealed record SuggestionDto(
        Guid? RecommendedUserId,
        double ConfidenceScore,
        string RoutingAdvice,
        List<CandidateDto> Candidates,
        bool Degraded,
        string Model);
}
