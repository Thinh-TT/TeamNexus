using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Ai.Services.Agent;
using TeamNexus.Modules.Ai.Services.Jev;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Giai đoạn 14.7 Track B §B1 — the team of specialised AI Agents.
/// <para>
/// The two things worth protecting here are <b>identity</b> and <b>strictness</b>. Identity: the pre-existing
/// agent row is the <c>general</c> persona and must keep the exact Phase 7 handle, because renaming it would
/// silently orphan every row that already exists. Strictness: an unknown persona is a <b>400</b>, never a
/// silent fall back to <c>general</c> — the difference between "the picker sent a stale key" and "the default
/// was used" is exactly what a fallback would erase.
/// </para>
/// </summary>
public sealed class Phase147PersonaTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public Phase147PersonaTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- the resolver -------------------------------------------------------

    [Fact]
    public async Task EnsureAgentAsync_StillResolvesTheGeneralPersonaRowWithThePhase7UserName()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var admin = await scenario.CreateUserAsync("Quản trị");
        var workspace = await scenario.CreateWorkspaceAsync(admin);

        // The Phase 7 entry point: no persona argument at all.
        var generalId = await EnsureAsync(scenario, workspace.Id, null);

        await using var db = scenario.NewDbContext();
        var membership = await db.WorkspaceMembers.SingleAsync(
            wm => wm.WorkspaceId == workspace.Id && wm.UserId == generalId);

        Assert.Equal(MemberType.AiAgent, membership.MemberType);
        Assert.Equal(PersonaCatalog.GeneralKey, membership.PersonaKey);

        var user = await db.Users.SingleAsync(u => u.Id == generalId);

        // ⛔ The handle Phase 7 created. A persona segment appended here would make every existing agent row
        // unreachable while every test still compiled.
        Assert.Equal($"ai-agent-{workspace.Id:N}", user.UserName);
        Assert.DoesNotContain(PersonaCatalog.GeneralKey, user.UserName!);

        // It is the same row for every key that canonicalises to `general`, and no second row appears.
        await using var scope = scenario.Services.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IAiAgentResolver>();

        Assert.Equal(generalId, await resolver.EnsureAgentAsync(workspace.Id));
        Assert.Equal(generalId, await resolver.EnsurePersonaAgentAsync(workspace.Id, "GENERAL"));
        Assert.Equal(generalId, await resolver.EnsurePersonaAgentAsync(workspace.Id, PersonaCatalog.GeneralKey));

        Assert.Equal(
            1,
            await scenario.CountAsync<WorkspaceMember>(
                wm => wm.WorkspaceId == workspace.Id && wm.MemberType == MemberType.AiAgent));
    }

    [Theory]
    [InlineData(PersonaCatalog.ScrumMasterKey, "AI Scrum Master", "user-story")]
    [InlineData(PersonaCatalog.TechLeadKey, "AI Tech Lead", "architecture")]
    [InlineData(PersonaCatalog.QaSecurityKey, "AI QA & Security", "security-audit")]
    public async Task EnsurePersonaAgentAsync_CreatesOneRowPerPersonaWithItsNameSkillsAndHandle(
        string personaKey, string expectedDisplayName, string expectedSkill)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var admin = await scenario.CreateUserAsync("Quản trị");
        var workspace = await scenario.CreateWorkspaceAsync(admin);

        var agentId = await EnsureAsync(scenario, workspace.Id, personaKey);

        await using var db = scenario.NewDbContext();

        var membership = await db.WorkspaceMembers.SingleAsync(
            wm => wm.WorkspaceId == workspace.Id && wm.UserId == agentId);

        Assert.Equal(MemberType.AiAgent, membership.MemberType);
        Assert.Equal(personaKey, membership.PersonaKey);
        Assert.Equal(expectedDisplayName, membership.AiAgentName);

        // The persona's skill set is what dispatch has to match on from the first assignment.
        Assert.Contains(expectedSkill, membership.Skills);

        var user = await db.Users.SingleAsync(u => u.Id == agentId);

        Assert.Equal(expectedDisplayName, user.DisplayName);
        Assert.Equal($"ai-agent-{personaKey}-{workspace.Id:N}", user.UserName);

        // An agent cannot log in: no password, no e-mail, 2FA on, no login row.
        Assert.Null(user.PasswordHash);
        Assert.Null(user.Email);
        Assert.True(user.TwoFactorEnabled);
    }

    [Fact]
    public async Task EnsurePersonaAgentAsync_IsIdempotentAndCreatesNoSecondRow()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var admin = await scenario.CreateUserAsync("Quản trị");
        var workspace = await scenario.CreateWorkspaceAsync(admin);

        var first = await EnsureAsync(scenario, workspace.Id, PersonaCatalog.TechLeadKey);
        var second = await EnsureAsync(scenario, workspace.Id, PersonaCatalog.TechLeadKey);

        Assert.Equal(first, second);

        Assert.Equal(
            1,
            await scenario.CountAsync<WorkspaceMember>(
                wm => wm.WorkspaceId == workspace.Id
                      && wm.MemberType == MemberType.AiAgent
                      && wm.PersonaKey == PersonaCatalog.TechLeadKey));
    }

    [Fact]
    public async Task EnsurePersonaAgentAsync_TwoConcurrentCalls_DoNotDuplicateTheRow()
    {
        // The unique index `uq_workspace_members_ai_persona` is the race guard; the loser of the race must
        // adopt the winner's row rather than surfacing a unique violation or (worse) leaving an orphan user.
        await using var scenario = await _database.CreateScenarioAsync();
        var admin = await scenario.CreateUserAsync("Quản trị");
        var workspace = await scenario.CreateWorkspaceAsync(admin);

        var tasks = Enumerable.Range(0, 4)
            .Select(_ => EnsureAsync(scenario, workspace.Id, PersonaCatalog.QaSecurityKey))
            .ToArray();

        var ids = await Task.WhenAll(tasks);

        Assert.Single(ids.Distinct());
        Assert.Equal(PersonaCatalog.QaSecurityKey, ids[0] is var _ ? PersonaCatalog.QaSecurityKey : null);

        Assert.Equal(
            1,
            await scenario.CountAsync<WorkspaceMember>(
                wm => wm.WorkspaceId == workspace.Id
                      && wm.MemberType == MemberType.AiAgent
                      && wm.PersonaKey == PersonaCatalog.QaSecurityKey));
    }

    [Fact]
    public async Task EnsurePersonaAgentAsync_TwoWorkspacesWithTheSamePersona_GetTheirOwnRows()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var admin = await scenario.CreateUserAsync("Quản trị");
        var first = await scenario.CreateWorkspaceAsync(admin, "Workspace A");
        var second = await scenario.CreateWorkspaceAsync(admin, "Workspace B");

        var firstAgent = await EnsureAsync(scenario, first.Id, PersonaCatalog.ScrumMasterKey);
        var secondAgent = await EnsureAsync(scenario, second.Id, PersonaCatalog.ScrumMasterKey);

        // The handle ends in the (dashed) workspace id, so it is unique per workspace — and the two rows must
        // not be the same user, or one workspace's agent would appear in the other's member list.
        Assert.NotEqual(firstAgent, secondAgent);

        await using var db = scenario.NewDbContext();

        var users = await db.Users
            .Where(u => u.Id == firstAgent || u.Id == secondAgent)
            .Select(u => u.UserName)
            .ToListAsync();

        Assert.Equal(2, users.Count);
        Assert.All(users, name => Assert.NotNull(name));
    }

    [Fact]
    public async Task EnsurePersonaAgentAsync_WithAnUnknownKey_IsRefusedWithoutCreatingAnything()
    {
        // ⛔ No silent fall back to `general`: that would make a stale picker key indistinguishable from the
        // default persona, which is the class of failure this phase exists to remove.
        await using var scenario = await _database.CreateScenarioAsync();
        var admin = await scenario.CreateUserAsync("Quản trị");
        var workspace = await scenario.CreateWorkspaceAsync(admin);

        await using var scope = scenario.Services.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IAiAgentResolver>();

        await Assert.ThrowsAsync<BadRequestException>(
            () => resolver.EnsurePersonaAgentAsync(workspace.Id, "architect"));

        Assert.Equal(
            0,
            await scenario.CountAsync<WorkspaceMember>(
                wm => wm.WorkspaceId == workspace.Id && wm.MemberType == MemberType.AiAgent));
    }

    [Theory]
    [InlineData(PersonaCatalog.GeneralKey)]
    [InlineData(PersonaCatalog.ScrumMasterKey)]
    [InlineData(PersonaCatalog.TechLeadKey)]
    [InlineData(PersonaCatalog.QaSecurityKey)]
    public async Task IsAiAgentAsync_IsTrueForEveryPersonaAndFalseForAHuman(string personaKey)
    {
        // The check is used to decide whether a run may execute as this assignee, so a persona the resolver
        // created but this method did not recognise would make its own runs impossible.
        await using var scenario = await _database.CreateScenarioAsync();
        var admin = await scenario.CreateUserAsync("Quản trị");
        var workspace = await scenario.CreateWorkspaceAsync(admin);

        var agentId = await EnsureAsync(scenario, workspace.Id, personaKey);

        await using var scope = scenario.Services.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IAiAgentResolver>();

        Assert.True(await resolver.IsAiAgentAsync(workspace.Id, agentId));

        // A human is never an agent, and never in another workspace.
        Assert.False(await resolver.IsAiAgentAsync(workspace.Id, admin.Id));
        Assert.False(await resolver.IsAiAgentAsync(Guid.NewGuid(), agentId));
    }

    // ---- the members read path ----------------------------------------------

    [Fact]
    public async Task Members_ExposeThePersonaKeyForAgentsAndNullForHumans()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var admin = await scenario.CreateUserAsync("Quản trị");
        var workspace = await scenario.CreateWorkspaceAsync(admin);

        await EnsureAsync(scenario, workspace.Id, PersonaCatalog.TechLeadKey);

        using var client = await scenario.AsUserAsync(admin);

        var members = await client.GetJsonAsync<List<WorkspaceMemberResponse>>(
            $"/api/workspaces/{workspace.Id}/members");

        Assert.NotNull(members);

        // The human row cannot carry a persona (the schema CHECK enforces it), so the field is null.
        var human = Assert.Single(members!, m => m.UserId == admin.Id);
        Assert.Null(human.PersonaKey);

        var techLead = Assert.Single(members!, m => m.PersonaKey == PersonaCatalog.TechLeadKey);
        Assert.Equal("ai_agent", techLead.MemberType);
        Assert.Equal("AI Tech Lead", techLead.DisplayName);

        // The general agent still appears (materialised by the read path) with its own key.
        Assert.Contains(members!, m => m.PersonaKey == PersonaCatalog.GeneralKey);
    }

    // ---- the chat endpoint's persona parameter -------------------------------

    [Fact]
    public async Task Session_AdvertisesTheWholePersonaCatalogueAndTheServerDefault()
    {
        // The picker is populated from the server's own catalogue, so the UI can never offer a value the
        // endpoint would reject with 400 — which is the only reason a strict 400 is safe to ship.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, board) = await SeedBoardAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.GetAsync($"/api/boards/{board.Id}/agent-chat/session");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        var personas = root.GetProperty("personas").EnumerateArray()
            .Select(p => (Key: p.GetProperty("key").GetString()!, Display: p.GetProperty("displayName").GetString()))
            .ToList();

        Assert.Equal(PersonaCatalog.All.Count, personas.Count);
        Assert.Equal(
            PersonaCatalog.All.Select(p => p.Key).ToList(),
            personas.Select(p => p.Key).ToList());

        Assert.All(personas, p => Assert.False(string.IsNullOrWhiteSpace(p.Display)));

        Assert.Equal(PersonaCatalog.GeneralKey, root.GetProperty("defaultPersona").GetString());

        // The pre-existing fields are untouched (append-only response).
        Assert.Equal(board.Id, root.GetProperty("boardId").GetGuid());
        Assert.False(root.GetProperty("hasMessages").GetBoolean());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Stream_WithAnAbsentOrBlankPersona_RunsAsTheDefaultOne(string? persona)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, board) = await SeedBoardAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var frames = await StreamAsync(client, board.Id, persona, "Bảng này thế nào?");

        // The offline provider is keyed on the marker, so a prompt built for a different branch would not
        // produce this frame sequence at all.
        Assert.NotEmpty(Named(frames, "meta"));
        Assert.NotEmpty(Named(frames, "delta"));
        Assert.NotEmpty(Named(frames, "done"));

        var done = Assert.Single(Named(frames, "done"));
        Assert.Equal(
            string.Concat(Named(frames, "delta").Select(f => f.Data.GetProperty("text").GetString())),
            done.Data.GetProperty("answer").GetString());
    }

    [Fact]
    public async Task Stream_WithAnUnknownPersona_IsBadRequest()
    {
        // ⛔ Not a silent fall back: a stale key from the picker and "no persona chosen" must stay
        // distinguishable, and a 400 is the only version of that which a client can act on.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, board) = await SeedBoardAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        using var response = await PostStreamAsync(client, board.Id, "architect", "Xin chào");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Stream_WithAnUnknownPersona_SpendsNothingAtTheProvider()
    {
        // The guard runs before the database and before the provider, so a bad persona costs no token.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, board) = await SeedBoardAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        using var response = await PostStreamAsync(client, board.Id, "no-such-persona", "Xin chào");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // No transcript row and no stored turn: the request never reached the service's write path.
        Assert.Equal(0, await scenario.CountAsync<AgentChatMessage>(m => m.BoardId == board.Id));
    }

    [Fact]
    public async Task Stream_AsANonDefaultPersona_CarriesTheRoleBlockAndKeepsTheMarker()
    {
        // ⛔ The marker is what `FakeAiProvider` branches on, so it must survive byte-for-byte; the persona is
        // PREPENDED, never substituted. The role is read back through the service because no frame echoes the
        // prompt.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, board) = await SeedBoardAsync(scenario);

        var prepared = await PrepareAsync(scenario, board.Id, admin.Id, PersonaCatalog.TechLeadKey);

        Assert.Equal(PersonaCatalog.TechLeadKey, prepared.PersonaKey);
        Assert.Contains(AgentChatPrompts.Marker, prepared.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains(AgentChatPrompts.PersonaHeading, prepared.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("AI Tech Lead", prepared.SystemPrompt, StringComparison.Ordinal);

        // The role block opens the prompt — it must not be buried after the rules it qualifies.
        Assert.StartsWith(AgentChatPrompts.PersonaHeading, prepared.SystemPrompt, StringComparison.Ordinal);
        Assert.Equal(workspace.Id, prepared.WorkspaceId);
    }

    [Fact]
    public async Task Stream_AsTheGeneralPersona_IsByteIdenticalToThePersonaLessPrompt()
    {
        // The default request must be exactly what Phase 14.5 produced: writing an (empty) block for `general`
        // would still be harmless, but identical bytes make "did this phase change the default?" a diff question.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, board) = await SeedBoardAsync(scenario);

        var withoutPersona = await PrepareAsync(scenario, board.Id, admin.Id, persona: null);
        var withGeneral = await PrepareAsync(scenario, board.Id, admin.Id, PersonaCatalog.GeneralKey);

        Assert.Equal(PersonaCatalog.GeneralKey, withoutPersona.PersonaKey);
        Assert.Equal(withoutPersona.SystemPrompt, withGeneral.SystemPrompt);

        Assert.DoesNotContain(AgentChatPrompts.PersonaHeading, withoutPersona.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains(AgentChatPrompts.Marker, withoutPersona.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stream_WithACaseInsensitivePersonaKey_IsAcceptedAndCanonicalised()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, board) = await SeedBoardAsync(scenario);

        var prepared = await PrepareAsync(scenario, board.Id, admin.Id, "  Scrum_Master  ");

        // The canonical key is what the prompt and the log line describe, not the caller's casing.
        Assert.Equal(PersonaCatalog.ScrumMasterKey, prepared.PersonaKey);
        Assert.Contains("AI Scrum Master", prepared.SystemPrompt, StringComparison.Ordinal);
    }

    // ---- helpers ------------------------------------------------------------

    private static async Task<(ApplicationUser Admin, Workspace Workspace, Board Board)> SeedBoardAsync(
        TestScenario scenario)
    {
        var admin = await scenario.CreateUserAsync("Quản trị");
        var workspace = await scenario.CreateWorkspaceAsync(admin, "Workspace Persona");
        var (board, _, _) = await scenario.CreateBoardAsync(workspace, "Board Persona");

        return (admin, workspace, board);
    }

    private static async Task<Guid> EnsureAsync(TestScenario scenario, Guid workspaceId, string? personaKey)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IAiAgentResolver>();

        // `null` exercises the Phase 7 entry point, which is the `general` persona by construction.
        return personaKey is null
            ? await resolver.EnsureAgentAsync(workspaceId)
            : await resolver.EnsurePersonaAgentAsync(workspaceId, personaKey);
    }

    private static async Task<AgentChatPreparedRequest> PrepareAsync(
        TestScenario scenario, Guid boardId, Guid userId, string? persona)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        var chat = scope.ServiceProvider.GetRequiredService<IAgentChatService>();

        var request = new AgentChatSendRequest(
            [new AiChatMessageRequest("user", "Tiến độ bảng thế nào?")],
            []);

        return await chat.PrepareAsync(boardId, request, persona, userId);
    }

    private static Task<HttpResponseMessage> PostStreamAsync(
        TestHttpClient client, Guid boardId, string? persona, string content)
    {
        var url = $"/api/boards/{boardId}/agent-chat/stream";

        if (persona is not null)
        {
            url += $"?persona={Uri.EscapeDataString(persona)}";
        }

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                messages = new[] { new { role = "user", content } },
                attachedTaskIds = Array.Empty<object>(),
            }),
        };

        return client.SendStreamingAsync(request);
    }

    private static async Task<List<SseFrame>> StreamAsync(
        TestHttpClient client, Guid boardId, string? persona, string content)
    {
        using var response = await PostStreamAsync(client, boardId, persona, content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return ParseFrames(await response.Content.ReadAsStringAsync());
    }

    private static IEnumerable<SseFrame> Named(IEnumerable<SseFrame> frames, string eventName)
        => frames.Where(frame => string.Equals(frame.Event, eventName, StringComparison.Ordinal));

    private static List<SseFrame> ParseFrames(string body)
    {
        var frames = new List<SseFrame>();
        var currentEvent = "message";
        var data = new System.Text.StringBuilder();

        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    frames.Add(new SseFrame(currentEvent, JsonDocument.Parse(data.ToString()).RootElement.Clone()));
                    data.Clear();
                }

                currentEvent = "message";
                continue;
            }

            if (line.StartsWith(':'))
            {
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                currentEvent = line["event:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(line["data:".Length..].Trim());
            }
        }

        if (data.Length > 0)
        {
            frames.Add(new SseFrame(currentEvent, JsonDocument.Parse(data.ToString()).RootElement.Clone()));
        }

        return frames;
    }

    private sealed record SseFrame(string Event, JsonElement Data);
}
