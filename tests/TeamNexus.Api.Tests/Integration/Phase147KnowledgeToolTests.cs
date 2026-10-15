using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Ai.Services.Agent;
using TeamNexus.Modules.Ai.Services.Jev;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Giai đoạn 14.7 Track B §B2 — the three new agent tools, driven through the real registry.
/// <para>
/// <b>Dispatched through <see cref="IAgentToolRegistry"/>, not called directly.</b> That is what makes the
/// assertions meaningful: the registry is where the whitelist, the argument parsing and the loop-control signals
/// live, so a tool that was registered under a name the whitelist does not carry would fail here rather than pass
/// a unit test that constructed it by hand.
/// </para>
/// <para>
/// <b>The workspace id always comes from <see cref="AgentToolContext"/> and never from the arguments</b>, which
/// is the security invariant every "must not leak across workspaces" assertion below is really testing.
/// </para>
/// </summary>
public sealed class Phase147KnowledgeToolTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public Phase147KnowledgeToolTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- the registry's own contract ---------------------------------------

    [Fact]
    public async Task EveryAdvertisedTool_HasAMatchingRegistration()
    {
        // ⛔ The guardrail the phase brief calls out: adding a schema without adding the implementation would
        // advertise a tool the model can call and the registry would answer "Unknown tool" — a silent capability
        // regression that no other test would notice.
        await using var scenario = await _database.CreateScenarioAsync();

        await using var scope = scenario.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var advertised = AgentToolDefinitions.All
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var registered = services.GetServices<IAgentTool>()
            .Select(tool => tool.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(7, advertised.Count);
        Assert.Equal(7, registered.Count);
        Assert.Equal(advertised, registered);
    }

    [Fact]
    public async Task Dispatch_WithAnUnwhitelistedName_IsAnErrorOutcomeRatherThanAThrow()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var context = NewContext();

        var outcome = await DispatchAsync(scenario, "DeleteEverything", "{}", context);

        Assert.True(outcome.IsError);
        Assert.Contains("Unknown tool", outcome.ResultJson, StringComparison.Ordinal);
        Assert.False(outcome.StopLoop);
    }

    [Fact]
    public async Task Dispatch_WithMalformedArgumentsJson_IsAnErrorOutcome()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var context = NewContext();

        var outcome = await DispatchAsync(scenario, AgentToolDefinitions.MatchSkillsName, "{not json", context);

        Assert.True(outcome.IsError);
        Assert.Contains("error", outcome.ResultJson, StringComparison.Ordinal);
    }

    // ---- InspectMemberWorkload ----------------------------------------------

    [Fact]
    public async Task InspectMemberWorkload_ReportsTheExactLoadOfAMember()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        // ⚠️ `InProgressCount` is every open card in a NON-DONE column, not "the card in the lane called Đang
        // làm" — `MemberWorkloadReader` treats anything that is not done (and not the clarification lane) as work
        // in flight. The fixture places the cards so the open and in-progress figures can be told apart.
        await scenario.CreateTaskAsync(seed.Board, seed.Todo, seed.Admin, "Việc trong Todo", assigneeId: seed.Dev.Id);
        await scenario.CreateTaskAsync(seed.Board, seed.Doing, seed.Admin, "Việc đang làm", assigneeId: seed.Dev.Id);
        await scenario.CreateTaskAsync(
            seed.Board, seed.Todo, seed.Admin, "Việc quá hạn", assigneeId: seed.Dev.Id,
            dueDate: DateTimeOffset.UtcNow.AddDays(-2));

        // A done card is neither open nor in progress.
        await scenario.CreateTaskAsync(seed.Board, seed.Done, seed.Admin, "Việc đã xong", assigneeId: seed.Dev.Id);

        // A card with no assignee is nobody's load.
        await scenario.CreateTaskAsync(seed.Board, seed.Todo, seed.Admin, "Việc chưa gán");

        var context = NewContext(seed.Workspace.Id, seed.Board.Id);

        var payload = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.InspectMemberWorkloadName,
            JsonSerializer.Serialize(new { userId = seed.Dev.Id }), context);

        Assert.True(payload.GetProperty("found").GetBoolean());
        Assert.Equal("Nguyễn Văn A", payload.GetProperty("displayName").GetString());
        Assert.Equal("Backend Dev", payload.GetProperty("primaryRole").GetString());

        Assert.Equal(3, payload.GetProperty("openTasks").GetInt32());
        Assert.Equal(1, payload.GetProperty("overdueTasks").GetInt32());

        // All three open cards sit in a non-done column, so all three count as in progress.
        Assert.Equal(3, payload.GetProperty("inProgressTasks").GetInt32());

        Assert.False(payload.GetProperty("isAiAgent").GetBoolean());

        // The raw numbers are reported, not a verdict: `atWipLimit` is derived from the stored limit.
        Assert.Equal(seed.DevMaxWip, payload.GetProperty("maxWipLimit").GetInt32());
        Assert.True(payload.GetProperty("atWipLimit").GetBoolean());
    }

    [Fact]
    public async Task InspectMemberWorkload_ReflectsTheAtWipLimitBoundary()
    {
        // The boundary is the interesting case: "reached the limit" is a nudge, "one under" is not.
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        async Task<JsonElement> InspectAsync()
        {
            var context = NewContext(seed.Workspace.Id, seed.Board.Id);

            return await DispatchJsonAsync(
                scenario, AgentToolDefinitions.InspectMemberWorkloadName,
                JsonSerializer.Serialize(new { userId = seed.Dev.Id }), context);
        }

        // The limit is 2 and the dev holds nothing yet.
        Assert.Equal(0, (await InspectAsync()).GetProperty("inProgressTasks").GetInt32());
        Assert.False((await InspectAsync()).GetProperty("atWipLimit").GetBoolean());

        await scenario.CreateTaskAsync(seed.Board, seed.Doing, seed.Admin, "Đang làm", assigneeId: seed.Dev.Id);

        // One card against a limit of two: still room.
        Assert.Equal(1, (await InspectAsync()).GetProperty("inProgressTasks").GetInt32());
        Assert.False((await InspectAsync()).GetProperty("atWipLimit").GetBoolean());

        await scenario.CreateTaskAsync(seed.Board, seed.Todo, seed.Admin, "Chờ làm", assigneeId: seed.Dev.Id);

        // Exactly at the limit: this is the boundary the field exists to report.
        Assert.Equal(2, (await InspectAsync()).GetProperty("inProgressTasks").GetInt32());
        Assert.True((await InspectAsync()).GetProperty("atWipLimit").GetBoolean());
    }

    [Fact]
    public async Task InspectMemberWorkload_WithNoConfiguredWipLimit_NeverReportsAtTheLimit()
    {
        // `MaxWipLimit = 0` means "no limit configured", which is NOT "at the limit" — treating it as reached
        // would nudge a manager about every member who simply has no cap.
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        await using (var db = scenario.NewDbContext())
        {
            var membership = await db.WorkspaceMembers.SingleAsync(
                wm => wm.WorkspaceId == seed.Workspace.Id && wm.UserId == seed.Dev.Id);

            membership.MaxWipLimit = 0;
            await db.SaveChangesAsync();
        }

        await scenario.CreateTaskAsync(seed.Board, seed.Doing, seed.Admin, "Đang làm", assigneeId: seed.Dev.Id);
        await scenario.CreateTaskAsync(seed.Board, seed.Todo, seed.Admin, "Chờ làm", assigneeId: seed.Dev.Id);

        var context = NewContext(seed.Workspace.Id, seed.Board.Id);

        var payload = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.InspectMemberWorkloadName,
            JsonSerializer.Serialize(new { userId = seed.Dev.Id }), context);

        Assert.Equal(0, payload.GetProperty("maxWipLimit").GetInt32());
        Assert.Equal(2, payload.GetProperty("inProgressTasks").GetInt32());
        Assert.False(payload.GetProperty("atWipLimit").GetBoolean());
    }

    [Fact]
    public async Task InspectMemberWorkload_ForANonMember_ReportsNotFoundRatherThanNumbers()
    {
        // ⛔ The leak this catches: answering with the workload of a user who is not on this team. "Not a member"
        // is information the model needs to route around, so it is a normal result rather than an error.
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        var outsider = await scenario.CreateUserAsync("Người ngoài");
        var otherWorkspace = await scenario.CreateWorkspaceAsync(outsider, "Workspace Khác");
        var (otherBoard, otherTodo, _) = await scenario.CreateBoardAsync(otherWorkspace, "Board Khác");
        await scenario.CreateTaskAsync(otherBoard, otherTodo, outsider, "Việc riêng", assigneeId: outsider.Id);

        var context = NewContext(seed.Workspace.Id, seed.Board.Id);

        var payload = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.InspectMemberWorkloadName,
            JsonSerializer.Serialize(new { userId = outsider.Id }), context);

        Assert.False(payload.GetProperty("found").GetBoolean());
        Assert.False(payload.TryGetProperty("openTasks", out _));
    }

    [Fact]
    public async Task InspectMemberWorkload_FlagsTheAiAgentRowAsAnAgent()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        var agentId = await EnsureAgentAsync(scenario, seed.Workspace.Id);

        var context = NewContext(seed.Workspace.Id, seed.Board.Id);

        var payload = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.InspectMemberWorkloadName,
            JsonSerializer.Serialize(new { userId = agentId }), context);

        Assert.True(payload.GetProperty("found").GetBoolean());
        Assert.True(payload.GetProperty("isAiAgent").GetBoolean());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"userId":""}""")]
    [InlineData("""{"userId":"   "}""")]
    [InlineData("""{"userId":"not-a-guid"}""")]
    [InlineData("""{"userId":"00000000-0000-0000-0000-000000000000"}""")]
    [InlineData("""{"userId":123}""")]
    public async Task InspectMemberWorkload_WithAMissingOrUnusableUserId_IsAnErrorObject(string arguments)
    {
        // The arguments come from the model, so this is the untrusted boundary: every shape must be answered with
        // an `error` object rather than an exception that would abort the run.
        await using var scenario = await _database.CreateScenarioAsync();
        var context = NewContext();

        var payload = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.InspectMemberWorkloadName, arguments, context);

        Assert.True(payload.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task InspectMemberWorkload_AgreesWithTheAssigneeSuggestionPathBecauseTheyShareOneQuery()
    {
        // §A4 and §B2 read the SAME `IMemberWorkloadReader`, so the agent's view of a member's load can never
        // disagree with the figure the dispatcher used. Asserted against the reader directly, which is exactly
        // the shared seam.
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        await scenario.CreateTaskAsync(seed.Board, seed.Todo, seed.Admin, "A", assigneeId: seed.Dev.Id);
        await scenario.CreateTaskAsync(seed.Board, seed.Doing, seed.Admin, "B", assigneeId: seed.Dev.Id);
        await scenario.CreateTaskAsync(
            seed.Board, seed.Todo, seed.Admin, "C", assigneeId: seed.Dev.Id,
            dueDate: DateTimeOffset.UtcNow.AddDays(-5));

        var context = NewContext(seed.Workspace.Id, seed.Board.Id);

        var payload = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.InspectMemberWorkloadName,
            JsonSerializer.Serialize(new { userId = seed.Dev.Id }), context);

        await using var scope = scenario.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider
            .GetRequiredService<TeamNexus.Modules.Ai.Services.IMemberWorkloadReader>();

        var measured = await reader.LoadOneAsync(seed.Workspace.Id, seed.Dev.Id);
        Assert.NotNull(measured);

        Assert.Equal(measured!.OpenTaskCount, payload.GetProperty("openTasks").GetInt32());
        Assert.Equal(measured.OverdueCount, payload.GetProperty("overdueTasks").GetInt32());
        Assert.Equal(measured.InProgressCount, payload.GetProperty("inProgressTasks").GetInt32());
        Assert.Equal(measured.MaxWipLimit, payload.GetProperty("maxWipLimit").GetInt32());
    }

    // ---- MatchSkills ---------------------------------------------------------

    [Fact]
    public async Task MatchSkills_RanksMembersWithAtLeastOneMatchingSlugAndReportsCoverage()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        await SetSkillsAsync(scenario, seed.Workspace.Id, seed.Dev.Id, ["csharp", "postgresql", "react"]);
        await SetSkillsAsync(scenario, seed.Workspace.Id, seed.Admin.Id, ["react", "css"]);

        var context = NewContext(seed.Workspace.Id, seed.Board.Id);

        var payload = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.MatchSkillsName,
            """{"skills":["csharp","postgresql"],"limit":5}""", context);

        var matches = payload.GetProperty("matches").EnumerateArray().ToList();

        // ⛔ Nobody with zero overlap: returning them would only add noise the model has to filter out, and the
        // whole point of the tool is to narrow the field.
        var dev = Assert.Single(matches, m => m.GetProperty("userId").GetGuid() == seed.Dev.Id);
        Assert.DoesNotContain(matches, m => m.GetProperty("userId").GetGuid() == seed.Admin.Id);

        Assert.Equal(2, dev.GetProperty("coveredSkills").EnumerateArray().Count());
        Assert.Empty(dev.GetProperty("missingSkills").EnumerateArray());
        Assert.Equal(1.0, dev.GetProperty("matchRatio").GetDouble());

        // The admin has overlap on neither requested slug, so the tool reports nobody rather than everybody.
        var adminOnly = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.MatchSkillsName,
            """{"skills":["css"]}""", context);

        var cssMatches = adminOnly.GetProperty("matches").EnumerateArray().ToList();
        var admin = Assert.Single(cssMatches);
        Assert.Equal(seed.Admin.Id, admin.GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task MatchSkills_ReportsThePartialCoverageRatioAndTheMissingSlugs()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        await SetSkillsAsync(scenario, seed.Workspace.Id, seed.Dev.Id, ["csharp"]);

        var context = NewContext(seed.Workspace.Id, seed.Board.Id);

        var payload = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.MatchSkillsName,
            """{"skills":["csharp","postgresql","react"]}""", context);

        var match = Assert.Single(payload.GetProperty("matches").EnumerateArray().ToList(), m => m.GetProperty("userId").GetGuid() == seed.Dev.Id);

        Assert.Equal(3, payload.GetProperty("requestedSkills").EnumerateArray().Count());

        // Rounded to 3 decimals on purpose: 0.3333333333333333 invites the model to quote meaningless precision.
        Assert.Equal(0.333, match.GetProperty("matchRatio").GetDouble(), 3);
        Assert.Equal(
            ["postgresql", "react"],
            match.GetProperty("missingSkills").EnumerateArray().Select(v => v.GetString()!).ToList());
    }

    [Fact]
    public async Task MatchSkills_DeduplicatesMixedCaseAndBlankSlugsCaseInsensitively()
    {
        // ⛔ The ratio is computed against this list, so letting "React" and "react" both count would inflate it
        // — and a member who knows react once would look like they cover two of the three requirements.
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        await SetSkillsAsync(scenario, seed.Workspace.Id, seed.Dev.Id, ["react"]);

        var context = NewContext(seed.Workspace.Id, seed.Board.Id);

        var payload = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.MatchSkillsName,
            """{"skills":["React","  react  ","REACT","","   "]}""", context);

        Assert.Equal(["react"], payload.GetProperty("requestedSkills").EnumerateArray().Select(v => v.GetString()!).ToList());

        var match = Assert.Single(payload.GetProperty("matches").EnumerateArray().ToList(), m => m.GetProperty("userId").GetGuid() == seed.Dev.Id);

        Assert.Equal(1.0, match.GetProperty("matchRatio").GetDouble());
        Assert.Empty(match.GetProperty("missingSkills").EnumerateArray());
    }

    [Fact]
    public async Task MatchSkills_AcceptsABareStringAsAOneElementList()
    {
        // A model that sends a scalar where an array was documented is making the intent obvious; the tolerant
        // read is what keeps a run alive over a formatting slip.
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        await SetSkillsAsync(scenario, seed.Workspace.Id, seed.Dev.Id, ["csharp"]);

        var context = NewContext(seed.Workspace.Id, seed.Board.Id);

        var payload = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.MatchSkillsName, """{"skills":"CSharp"}""", context);

        Assert.Contains(
            payload.GetProperty("matches").EnumerateArray(),
            m => m.GetProperty("userId").GetGuid() == seed.Dev.Id);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"skills":[]}""")]
    [InlineData("""{"skills":[""]}""")]
    [InlineData("""{"skills":["   "]}""")]
    [InlineData("""{"skills":null}""")]
    [InlineData("""{"skills":[1,2,3]}""")]
    public async Task MatchSkills_WithNoUsableSkill_IsAnErrorObject(string arguments)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var context = NewContext();

        var payload = await DispatchJsonAsync(scenario, AgentToolDefinitions.MatchSkillsName, arguments, context);

        Assert.True(payload.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task MatchSkills_ClampsTheLimitIntoTheAdvertisedRange()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        var context = NewContext(seed.Workspace.Id, seed.Board.Id);

        // Every member of the workspace owns the slug, so the limit is the only thing that can narrow the list.
        for (var index = 0; index < 15; index++)
        {
            var member = await scenario.CreateUserAsync($"Thành viên {index}");
            await AddMemberAsync(scenario, seed.Workspace.Id, member.Id);
            await SetSkillsAsync(scenario, seed.Workspace.Id, member.Id, ["csharp"]);
        }

        var huge = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.MatchSkillsName,
            """{"skills":["csharp"],"limit":9999}""", context);

        Assert.True(huge.GetProperty("matches").EnumerateArray().Count() <= AgentToolDefinitions.MaxSkillMatches);

        var one = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.MatchSkillsName,
            """{"skills":["csharp"],"limit":1}""", context);

        Assert.Single(one.GetProperty("matches").EnumerateArray().ToList());

        var zero = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.MatchSkillsName,
            """{"skills":["csharp"],"limit":0}""", context);

        Assert.Single(zero.GetProperty("matches").EnumerateArray().ToList());
    }

    [Fact]
    public async Task MatchSkills_NeverReturnsMembersOfAnotherWorkspace()
    {
        // ⛔ The leak this catches: ranking a global member table would let one workspace's agent see who works
        // in another — the exact cross-workspace disclosure every tool in this file is written to prevent.
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        var outsider = await scenario.CreateUserAsync("Người ngoài");
        var otherWorkspace = await scenario.CreateWorkspaceAsync(outsider, "Workspace Khác");
        await SetSkillsAsync(scenario, otherWorkspace.Id, outsider.Id, ["csharp"]);

        var context = NewContext(seed.Workspace.Id, seed.Board.Id);

        var payload = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.MatchSkillsName, """{"skills":["csharp"]}""", context);

        Assert.DoesNotContain(
            payload.GetProperty("matches").EnumerateArray(),
            m => m.GetProperty("userId").GetGuid() == outsider.Id);
    }

    [Fact]
    public async Task MatchSkills_IsAReadOnlyToolThatDoesNotStopTheLoop()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);

        await SetSkillsAsync(scenario, seed.Workspace.Id, seed.Dev.Id, ["csharp"]);

        var context = NewContext(seed.Workspace.Id, seed.Board.Id);

        var outcome = await DispatchAsync(
            scenario, AgentToolDefinitions.MatchSkillsName, """{"skills":["csharp"]}""", context);

        // Only DraftOutput and RequestClarification may end a run.
        Assert.False(outcome.StopLoop);
        Assert.False(outcome.IsError);
        Assert.Null(outcome.StopReason);
    }

    // ---- ValidateAcceptanceCriteria -----------------------------------------

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"draft":""}""")]
    [InlineData("""{"draft":"   "}""")]
    [InlineData("""{"draft":null}""")]
    public async Task ValidateAcceptanceCriteria_WithNoUsableDraft_IsAnErrorObject(string arguments)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var context = NewContext();

        var payload = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.ValidateAcceptanceCriteriaName, arguments, context);

        Assert.True(payload.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task ValidateAcceptanceCriteria_WithJevOff_ReturnsNotVerifiedWithoutThrowing()
    {
        // ⛔ The whole reason this tool may be called mid-run: Jev is optional assistance, so a disabled
        // provider must produce a normal result rather than an error. Throwing would let an optional second
        // opinion kill work the model had already completed.
        await using var scenario = await _database.CreateScenarioAsync(jevEnabled: false);
        var context = NewContext();

        var payload = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.ValidateAcceptanceCriteriaName,
            """{"draft":"Bản nháp đã xong.","criteria":"Phải có kiểm thử."}""", context);

        Assert.True(payload.TryGetProperty("verified", out var verified));
        Assert.Equal(JsonValueKind.Null, verified.ValueKind);
        Assert.Contains("Không xác minh được", payload.GetProperty("note").GetString()!, StringComparison.Ordinal);
        Assert.Equal(ConfidenceRouting.ManualRequired, payload.GetProperty("routingAdvice").GetString());
    }

    [Fact]
    public async Task ValidateAcceptanceCriteria_WithJevReportingFailure_AlsoReturnsNotVerified()
    {
        // The provider-failed branch, reached through the offline provider's own error sentinel in the state.
        await using var scenario = await _database.CreateScenarioAsync();
        var context = NewContext();

        var payload = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.ValidateAcceptanceCriteriaName,
            """{"draft":"FAKE:JEV_ERROR — nội dung làm provider lỗi."}""", context);

        Assert.Equal(JsonValueKind.Null, payload.GetProperty("verified").ValueKind);
        Assert.Contains("Không xác minh được", payload.GetProperty("note").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAcceptanceCriteria_WithTheOfflineProvider_ReturnsABooleanVerdict()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var context = NewContext();

        var payload = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.ValidateAcceptanceCriteriaName,
            """{"draft":"Bản nháp đầy đủ.","criteria":"Có kiểm thử."}""", context);

        // A real verdict, not the degraded one: `verified` is a boolean here.
        Assert.True(payload.TryGetProperty("verified", out var verified));
        Assert.Equal(JsonValueKind.True, verified.ValueKind);

        Assert.True(payload.TryGetProperty("confidence", out var confidence));
        Assert.Equal(JsonValueKind.Number, confidence.ValueKind);

        // The note must say the verdict is advice, so the model cannot read a low-confidence "false" as a hard
        // rejection of work it could still submit.
        Assert.Contains("tham khảo", payload.GetProperty("note").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAcceptanceCriteria_TruncatesAnOverlongDraftBeforeCallingJev()
    {
        // ⛔ Jev bills input tokens, and a 400 KB draft would be billed in full while adding no decision signal
        // past the first few thousand characters. The cap is asserted on the REQUEST, because the tool's own
        // answer is a fixed two-line object that would look identical either way.
        var recorder = new RecordingJevDecisionProvider();

        await using var scenario = await _database.CreateScenarioAsync(jevProvider: recorder);
        var context = NewContext();

        var draft = new string('d', AgentToolDefinitions.MaxValidationDraftChars + 1_500);

        var payload = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.ValidateAcceptanceCriteriaName,
            JsonSerializer.Serialize(new { draft }), context);

        Assert.True(payload.TryGetProperty("verified", out _));
        Assert.Equal(1, recorder.CallCount);

        var state = recorder.LastRequest!.StateJson;

        using var document = JsonDocument.Parse(state);
        var sent = document.RootElement.GetProperty("draft").GetString();

        Assert.NotNull(sent);
        Assert.Equal(AgentToolDefinitions.MaxValidationDraftChars, sent!.Length);
        Assert.Equal(draft[..AgentToolDefinitions.MaxValidationDraftChars], sent);
    }

    [Fact]
    public async Task ValidateAcceptanceCriteria_SendsOnlyTheDraftAndTheCriteria()
    {
        // No ids, no board or member data: the model cannot hand Jev something Jev has no use for, and the
        // prompt-scoped invariant (an id the model never received cannot be echoed) holds here too.
        var recorder = new RecordingJevDecisionProvider();

        await using var scenario = await _database.CreateScenarioAsync(jevProvider: recorder);
        var seed = await SeedAsync(scenario);
        var context = NewContext(seed.Workspace.Id, seed.Board.Id, seed.Task.Id);

        await DispatchJsonAsync(
            scenario, AgentToolDefinitions.ValidateAcceptanceCriteriaName,
            """{"draft":"Nháp","criteria":"Tiêu chí"}""", context);

        var state = recorder.LastRequest!.StateJson;

        // Parsed rather than substring-matched: the tool serializes with `JsonSerializerDefaults.Web`, which
        // escapes non-ASCII, so the raw text carries the two fields as they were written — including both
        // Vietnamese values — after a round-trip through the JSON parser.
        using var document = JsonDocument.Parse(state);

        Assert.Equal("Nháp", document.RootElement.GetProperty("draft").GetString());
        Assert.Equal("Tiêu chí", document.RootElement.GetProperty("acceptanceCriteria").GetString());

        // Not one id from the context may appear anywhere in what Jev was sent.
        Assert.DoesNotContain(seed.Workspace.Id.ToString(), state, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(seed.Board.Id.ToString(), state, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(seed.Task.Id.ToString(), state, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workspaceId", state, StringComparison.OrdinalIgnoreCase);

        // And exactly one typed question was asked.
        Assert.Single(recorder.LastRequest.Questions);
        Assert.Equal(JevQuestionType.Noul, recorder.LastRequest.Questions[0].Type);
    }

    [Fact]
    public async Task ValidateAcceptanceCriteria_WithJevOff_NeverCallsTheProvider()
    {
        // A disabled feature must not spend a token to discover it is disabled: the tool checks the switch
        // before building a request at all.
        await using var scenario = await _database.CreateScenarioAsync(jevEnabled: false);
        var context = NewContext();

        var payload = await DispatchJsonAsync(
            scenario, AgentToolDefinitions.ValidateAcceptanceCriteriaName,
            """{"draft":"Nháp"}""", context);

        // The degraded answer is the observable proof the call never happened.
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("verified").ValueKind);
        Assert.Contains("Jev đang tắt", payload.GetProperty("note").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAcceptanceCriteria_ReturnsFalseRatherThanStoppingTheLoop()
    {
        // The description says so explicitly, and it matters: a run that ends without DraftOutput or
        // RequestClarification is a run with no output at all. Nothing this tool returns may set the loop's
        // stop signal, and it must not write a draft on the caller's behalf either.
        await using var scenario = await _database.CreateScenarioAsync();
        var context = NewContext();

        Assert.Null(context.Draft);

        var outcome = await DispatchAsync(
            scenario, AgentToolDefinitions.ValidateAcceptanceCriteriaName,
            """{"draft":"Bản nháp hoàn chỉnh."}""", context);

        Assert.False(outcome.StopLoop);
        Assert.Null(outcome.StopReason);

        // ⛔ It does NOT become the run's output: only `DraftOutput` may do that, and a model that reads an
        // "approved" verdict as permission to stop would otherwise end the run with a validation note as its
        // deliverable.
        Assert.Null(context.Draft);
    }

    // ---- guardrails ---------------------------------------------------------

    [Fact]
    public async Task AddingThreeTools_DidNotWidenAnyAgentGuardrail()
    {
        // ⛔ Asserted against the RESOLVED options of a host that carries the production values, not against the
        // defaults of a freshly constructed POCO: this is the configuration the running app actually uses.
        await using var scenario = await _database.CreateScenarioAsync();

        await using var scope = scenario.Services.CreateAsyncScope();
        var effective = scope.ServiceProvider.GetRequiredService<IOptions<AgentOptions>>().Value.Effective;

        Assert.Equal(15, effective.MaxToolCalls);
        Assert.Equal(300, effective.RunTimeoutSeconds);
        Assert.Equal(50_000, effective.MaxRunTokens);
        Assert.Equal(20, effective.MaxRunLlmCalls);

        // The registry advertises seven tools inside that single 15-call budget — it counts calls, it does not
        // multiply budgets.
        Assert.Equal(7, AgentToolDefinitions.All.Count);
        Assert.True(AgentToolDefinitions.All.Count <= effective.MaxToolCalls);
    }

    // ---- helpers ------------------------------------------------------------

    private sealed record Seed(
        ApplicationUser Admin,
        ApplicationUser Dev,
        Workspace Workspace,
        Board Board,
        BoardColumn Todo,
        BoardColumn Doing,
        BoardColumn Done,
        BoardTask Task,
        int DevMaxWip);

    private static async Task<Seed> SeedAsync(TestScenario scenario)
    {
        var admin = await scenario.CreateUserAsync("Quản trị");
        var dev = await scenario.CreateUserAsync("Nguyễn Văn A");

        var workspace = await scenario.CreateWorkspaceAsync(admin, "Workspace Công Cụ", (dev, WorkspaceRole.Member));

        var (board, todo, done) = await scenario.CreateBoardAsync(workspace, "Board Công Cụ");

        // A third lane that is neither done nor a clarification column, so "in progress" is observable. It is
        // created directly because `CreateBoardAsync` deliberately offers only the two ends — and its position
        // must be free, since `(board_id, position)` is unique and the two seeded lanes already hold 0 and 1.
        var doing = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Name = "Đang làm",
            Position = 2,
            IsDone = false,
        };

        await using (var db = scenario.NewDbContext())
        {
            db.BoardColumns.Add(doing);

            var membership = await db.WorkspaceMembers
                .SingleAsync(wm => wm.WorkspaceId == workspace.Id && wm.UserId == dev.Id);

            membership.Skills = ["csharp", "postgresql"];
            membership.PrimaryRole = "Backend Dev";
            // Two, so the boundary case can be reached in two steps (one card = room, two = at the limit) and the
            // "no limit configured" case can be asserted separately.
            membership.MaxWipLimit = 2;

            await db.SaveChangesAsync();
        }

        var task = await scenario.CreateTaskAsync(board, todo, admin, "Thẻ mẫu");

        return new Seed(admin, dev, workspace, board, todo, doing, done, task, DevMaxWip: 2);
    }

    private static async Task SetSkillsAsync(TestScenario scenario, Guid workspaceId, Guid userId, string[] skills)
    {
        await using var db = scenario.NewDbContext();

        var membership = await db.WorkspaceMembers
            .SingleAsync(wm => wm.WorkspaceId == workspaceId && wm.UserId == userId);

        membership.Skills = [.. skills];
        await db.SaveChangesAsync();
    }

    private static async Task AddMemberAsync(TestScenario scenario, Guid workspaceId, Guid userId)
    {
        await using var db = scenario.NewDbContext();

        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspaceId,
            UserId = userId,
            Role = WorkspaceRole.Member,
            MemberType = MemberType.Human,
            JoinedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    private static async Task<Guid> EnsureAgentAsync(TestScenario scenario, Guid workspaceId)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        var resolver = scope.ServiceProvider
            .GetRequiredService<TeamNexus.Modules.Board.Services.IAiAgentResolver>();

        return await resolver.EnsureAgentAsync(workspaceId);
    }

    private static AgentToolContext NewContext(Guid? workspaceId = null, Guid? boardId = null, Guid? taskId = null)
        => new(
            workspaceId ?? Guid.NewGuid(),
            boardId ?? Guid.NewGuid(),
            taskId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());

    private static async Task<AgentToolOutcome> DispatchAsync(
        TestScenario scenario, string name, string argumentsJson, AgentToolContext context)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IAgentToolRegistry>();

        return await registry.DispatchAsync(name, argumentsJson, context);
    }

    private static async Task<JsonElement> DispatchJsonAsync(
        TestScenario scenario, string name, string argumentsJson, AgentToolContext context)
    {
        var outcome = await DispatchAsync(scenario, name, argumentsJson, context);

        using var document = JsonDocument.Parse(outcome.ResultJson);

        return document.RootElement.Clone();
    }
}
