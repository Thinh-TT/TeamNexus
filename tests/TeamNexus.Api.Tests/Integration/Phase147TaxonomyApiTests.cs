using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Giai đoạn 14.7 §A2 — the fixed workspace vocabulary (Trụ cột 1) and the member capacity profile.
/// <para>
/// These two surfaces are what make Jev's <c>Choice</c> questions answerable at all: without a fixed
/// dictionary the same domain fragments into several options, and without a skill list the dispatcher can
/// only rank people by name. The tests therefore focus on the properties that matter to the AI path —
/// the option list is non-empty, stable and workspace-scoped — plus the guards that keep a casual caller
/// from emptying it.
/// </para>
/// </summary>
public sealed class Phase147TaxonomyApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public Phase147TaxonomyApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    /// <summary>
    /// One admin + one plain member + a workspace. Deliberately always uses the default workspace name:
    /// an earlier version passed a second string here and it silently bound to something other than
    /// <c>name</c>, which is not worth debugging again for a display label no assertion reads.
    /// </summary>
    private static async Task<(ApplicationUser Admin, ApplicationUser Member, Workspace Workspace)> SeedAsync(
        TestScenario scenario)
    {
        var admin = await scenario.CreateUserAsync("Quản trị");
        var member = await scenario.CreateUserAsync("Thành viên");
        var workspace = await scenario.CreateWorkspaceAsync(
            admin, others: [(member, WorkspaceRole.Member)]);

        return (admin, member, workspace);
    }

    // ---- seeding ------------------------------------------------------------------------------

    [Fact]
    public async Task GetTaxonomies_OnAFreshWorkspace_SeedsBothDefaultKinds()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (_, member, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(member);

        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}/taxonomies");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TaxonomyDto>();

        Assert.NotNull(body);
        Assert.True(body!.Seeded);

        // The seeded vocabulary is the option list Jev is handed, so it must be non-empty for BOTH kinds:
        // an empty kind silently makes every question of that type unanswerable.
        Assert.NotEmpty(body.Domains);
        Assert.NotEmpty(body.WorkTypes);

        Assert.Equal(
            TaxonomyDefaults.Domains.Select(seed => seed.Key),
            body.Domains.Select(entry => entry.Key));
        Assert.Equal(
            TaxonomyDefaults.WorkTypes.Select(seed => seed.Key),
            body.WorkTypes.Select(entry => entry.Key));

        // Positions are what the model's option order is built from, so they must be present and ascending.
        Assert.Equal(
            Enumerable.Range(0, body.Domains.Count),
            body.Domains.Select(entry => entry.Position));
    }

    [Fact]
    public async Task GetTaxonomies_IsIdempotentAndOnlyReportsSeededOnce()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (_, member, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(member);

        var first = await client.GetJsonAsync<TaxonomyDto>($"/api/workspaces/{workspace.Id}/taxonomies");
        var second = await client.GetJsonAsync<TaxonomyDto>($"/api/workspaces/{workspace.Id}/taxonomies");

        Assert.True(first!.Seeded);
        Assert.False(second!.Seeded);

        // The critical property: a second read must NOT re-insert and duplicate the dictionary.
        Assert.Equal(first.Domains.Count, second.Domains.Count);
        Assert.Equal(first.WorkTypes.Count, second.WorkTypes.Count);

        var stored = await scenario.CountAsync<WorkspaceTaxonomy>(x => x.WorkspaceId == workspace.Id);
        Assert.Equal(
            TaxonomyDefaults.Domains.Count + TaxonomyDefaults.WorkTypes.Count,
            stored);
    }

    [Fact]
    public async Task GetTaxonomies_WhenSeededConcurrently_StillProducesOneDictionary()
    {
        // The unique index is the race guard, not the pre-read: two simultaneous first reads must not
        // double the vocabulary. Reproduced by firing both requests before either can commit.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, member, workspace) = await SeedAsync(scenario);

        using var first = await scenario.AsUserAsync(admin);
        using var second = await scenario.AsUserAsync(member);

        var responses = await Task.WhenAll(
            first.GetAsync($"/api/workspaces/{workspace.Id}/taxonomies"),
            second.GetAsync($"/api/workspaces/{workspace.Id}/taxonomies"));

        foreach (var response in responses)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(
                response.StatusCode == HttpStatusCode.OK,
                $"Expected OK, got {(int)response.StatusCode}. Body: {body}");
        }

        var stored = await scenario.CountAsync<WorkspaceTaxonomy>(x => x.WorkspaceId == workspace.Id);
        Assert.Equal(
            TaxonomyDefaults.Domains.Count + TaxonomyDefaults.WorkTypes.Count,
            stored);
    }

    [Fact]
    public async Task GetTaxonomies_IsScopedToTheWorkspace()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        // Two fully independent workspaces, each created through the same proven seed path.
        var (adminA, _, workspaceA) = await SeedAsync(scenario);
        var (adminB, _, workspaceB) = await SeedAsync(scenario);

        // ⚠️ Both requests are issued ONE AT A TIME on purpose. `TestScenario.AsUserAsync` writes the token
        // into a scenario-shared cookie jar, so building two clients up front means the second call clobbers
        // the first user's cookie and the already-built client A would then be acting as user B (a 404 that
        // looks like a scoping bug and is not one).
        TaxonomyDto? a;
        TaxonomyDto? b;

        using (var clientA = await scenario.AsUserAsync(adminA))
        {
            a = await clientA.GetJsonAsync<TaxonomyDto>($"/api/workspaces/{workspaceA.Id}/taxonomies");
        }

        using (var clientB = await scenario.AsUserAsync(adminB))
        {
            b = await clientB.GetJsonAsync<TaxonomyDto>($"/api/workspaces/{workspaceB.Id}/taxonomies");
        }

        Assert.NotNull(a);
        Assert.NotNull(b);

        var storedA = await scenario.CountAsync<WorkspaceTaxonomy>(x => x.WorkspaceId == workspaceA.Id);
        var storedB = await scenario.CountAsync<WorkspaceTaxonomy>(x => x.WorkspaceId == workspaceB.Id);

        // Each workspace owns its rows; the query filter must not leak one workspace's entries into the other.
        Assert.Equal(a!.Domains.Count + a.WorkTypes.Count, storedA);
        Assert.Equal(b!.Domains.Count + b.WorkTypes.Count, storedB);

        // …and they are genuinely different rows, not one shared dictionary.
        Assert.DoesNotContain(
            a.Domains.Select(entry => entry.Id),
            id => b.Domains.Any(other => other.Id == id));
    }

    // ---- authorization ------------------------------------------------------------------------

    [Fact]
    public async Task GetTaxonomies_WithAUserOutsideTheWorkspace_IsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (_, _, workspace) = await SeedAsync(scenario);
        var outsider = await scenario.CreateUserAsync("Người ngoài");
        using var client = await scenario.AsUserAsync(outsider);

        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}/taxonomies");

        // 404 rather than 403: an outsider must not learn that the workspace exists (the WorkspaceAccess rule).
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReplaceTaxonomies_ByPlainMember_IsForbidden()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (_, member, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(member);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/taxonomies",
            new { domains = new[] { new { key = "Backend", label = "Backend" } }, workTypes = new[] { new { key = "Bug", label = "Bug" } } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- replace ------------------------------------------------------------------------------

    [Fact]
    public async Task ReplaceTaxonomies_ByManager_ReplacesExactlyWhatWasSent()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, member, workspace) = await SeedAsync(scenario);

        // A Manager, not the owner: the write gate is Manager+, and the owner is Admin.
        await using (var db = scenario.NewDbContext())
        {
            var membership = await db.WorkspaceMembers
                .FirstAsync(wm => wm.WorkspaceId == workspace.Id && wm.UserId == member.Id);
            membership.Role = WorkspaceRole.Manager;
            await db.SaveChangesAsync();
        }

        using var client = await scenario.AsUserAsync(member);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/taxonomies",
            new
            {
                domains = new[]
                {
                    // The cast on the FIRST element is what makes the array's inferred element type use
                    // `string?` — without it the null on the second element is a nullability warning.
                    new { key = "Backend", label = "Máy chủ", colorHex = (string?)"#1677ff" },
                    new { key = "Data", label = "Dữ liệu", colorHex = (string?)null },
                },
                workTypes = new[] { new { key = "Bug", label = "Lỗi", colorHex = "#ff4d4f" } },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<TaxonomyDto>();
        Assert.NotNull(body);
        Assert.False(body!.Seeded);   // a deliberate curation, not the default seed

        Assert.Equal(["Backend", "Data"], body.Domains.Select(entry => entry.Key));
        Assert.Equal(["Bug"], body.WorkTypes.Select(entry => entry.Key));

        // The label is a display value the model also sees as the option description, so a rename must
        // survive — it is not the machine key.
        Assert.Equal("Máy chủ", body.Domains[0].Label);

        // Replace semantics: the six work types that were seeded are gone, not merged.
        var stored = await scenario.CountAsync<WorkspaceTaxonomy>(x => x.WorkspaceId == workspace.Id);
        Assert.Equal(3, stored);
    }

    [Fact]
    public async Task ReplaceTaxonomies_WithAnEmptyKind_IsRejected()
    {
        // An empty vocabulary makes every `Choice` question unanswerable, which would silently switch the
        // whole dispatching feature off. A loud 400 is the correct answer.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var emptyWorkTypes = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/taxonomies",
            new { domains = new[] { new { key = "Backend", label = "Backend" } }, workTypes = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, emptyWorkTypes.StatusCode);

        var emptyDomains = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/taxonomies",
            new { domains = Array.Empty<object>(), workTypes = new[] { new { key = "Bug", label = "Bug" } } });

        Assert.Equal(HttpStatusCode.BadRequest, emptyDomains.StatusCode);

        // Nothing was written by either rejected call.
        var stored = await scenario.CountAsync<WorkspaceTaxonomy>(x => x.WorkspaceId == workspace.Id);
        Assert.Equal(0, stored);
    }

    [Fact]
    public async Task ReplaceTaxonomies_WithDuplicateKeys_IsBadRequestNotA500()
    {
        // The unique index would turn this into a 500; naming the offender is the useful answer.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/taxonomies",
            new
            {
                domains = new[]
                {
                    new { key = "Backend", label = "Backend" },
                    new { key = "backend", label = "Trùng" },
                },
                workTypes = new[] { new { key = "Bug", label = "Bug" } },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReplaceTaxonomies_WithBadColorOrBlankLabel_IsBadRequest()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var badColor = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/taxonomies",
            new
            {
                domains = new[] { new { key = "Backend", label = "Backend", colorHex = "red" } },
                workTypes = new[] { new { key = "Bug", label = "Bug" } },
            });

        Assert.Equal(HttpStatusCode.BadRequest, badColor.StatusCode);

        var blankLabel = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/taxonomies",
            new
            {
                domains = new[] { new { key = "Backend", label = "   " } },
                workTypes = new[] { new { key = "Bug", label = "Bug" } },
            });

        Assert.Equal(HttpStatusCode.BadRequest, blankLabel.StatusCode);
    }

    [Fact]
    public async Task ReplaceTaxonomies_ThenGet_KeepsTheReplacedVocabulary()
    {
        // Round-trip: the replaced vocabulary must survive a later read that would otherwise re-seed. This is
        // the regression that matters — a naive "seed when empty" would resurrect the defaults.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/taxonomies",
            new
            {
                domains = new[] { new { key = "OnlyOne", label = "Chỉ một" } },
                workTypes = new[] { new { key = "OnlyKind", label = "Chỉ một loại" } },
            });

        var body = await client.GetJsonAsync<TaxonomyDto>($"/api/workspaces/{workspace.Id}/taxonomies");

        Assert.NotNull(body);
        Assert.False(body!.Seeded);
        Assert.Equal(["OnlyOne"], body.Domains.Select(entry => entry.Key));
        Assert.Equal(["OnlyKind"], body.WorkTypes.Select(entry => entry.Key));
    }

    // ---- DTOs ---------------------------------------------------------------------------------

    private sealed record TaxonomyEntryDto(Guid Id, string Kind, string Key, string Label, string? ColorHex, int Position);

    private sealed record TaxonomyDto(
        Guid WorkspaceId,
        List<TaxonomyEntryDto> Domains,
        List<TaxonomyEntryDto> WorkTypes,
        bool Seeded);
}

/// <summary>
/// Giai đoạn 14.7 §A2 — the member capacity profile: validation rules, the Admin-only gate, the
/// AI-Agent refusal, and the leave-unchanged semantics of an absent field.
/// </summary>
public sealed class Phase147MemberProfileApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public Phase147MemberProfileApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    private static async Task<(ApplicationUser Admin, ApplicationUser Member, Workspace Workspace)> SeedAsync(
        TestScenario scenario)
    {
        var admin = await scenario.CreateUserAsync("Quản trị");
        var member = await scenario.CreateUserAsync("Thành viên");
        var workspace = await scenario.CreateWorkspaceAsync(
            admin, "Workspace Hồ Sơ", (member, WorkspaceRole.Member));

        return (admin, member, workspace);
    }

    private static async Task<WorkspaceMember> LoadMemberAsync(
        TestScenario scenario, Guid workspaceId, Guid userId)
    {
        await using var db = scenario.NewDbContext();

        return await db.WorkspaceMembers
            .AsNoTracking()
            .FirstAsync(wm => wm.WorkspaceId == workspaceId && wm.UserId == userId);
    }

    [Fact]
    public async Task UpdateProfile_ByAdmin_StoresSkillsRoleCapacityAndWipLimit()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, member, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{member.Id}/profile",
            new
            {
                skills = new[] { "csharp", "dotnet", "postgresql" },
                primaryRole = "Backend Dev",
                capacityWeight = 0.5,
                maxWipLimit = 3,
            });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var stored = await LoadMemberAsync(scenario, workspace.Id, member.Id);

        Assert.Equal(["csharp", "dotnet", "postgresql"], stored.Skills);
        Assert.Equal("Backend Dev", stored.PrimaryRole);
        Assert.Equal(0.5, stored.CapacityWeight);
        Assert.Equal(3, stored.MaxWipLimit);

        // The change is auditable: the activity feed names the fields that moved.
        var activity = await scenario.CountAsync<ActivityLog>(
            a => a.WorkspaceId == workspace.Id
                 && a.Action == ObserverActivityActions.MemberSkillsChanged);
        Assert.Equal(1, activity);
    }

    [Fact]
    public async Task UpdateProfile_NormalisesSkillCasingAndDeduplicates()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, member, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{member.Id}/profile",
            new { skills = new[] { "CSharp", "csharp", "  DotNet  " } });

        var stored = await LoadMemberAsync(scenario, workspace.Id, member.Id);

        // Case-folded and de-duplicated, so "CSharp" and "csharp" cannot coexist and split the matching.
        Assert.Equal(["csharp", "dotnet"], stored.Skills);
    }

    [Fact]
    public async Task UpdateProfile_WithAnAbsentField_LeavesItUnchanged()
    {
        // The Phase 13 §P4 precedent: a client editing only skills must not silently reset a colleague's
        // WIP limit back to the default.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, member, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{member.Id}/profile",
            new { skills = new[] { "react" }, primaryRole = "Frontend Dev", maxWipLimit = 9 });

        var afterFirst = await LoadMemberAsync(scenario, workspace.Id, member.Id);
        Assert.Equal(9, afterFirst.MaxWipLimit);

        // Only the skills field is present this time.
        await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{member.Id}/profile",
            new { skills = new[] { "react", "typescript" } });

        var afterSecond = await LoadMemberAsync(scenario, workspace.Id, member.Id);

        Assert.Equal(["react", "typescript"], afterSecond.Skills);
        Assert.Equal(9, afterSecond.MaxWipLimit);            // preserved, not reset to the default 5
        Assert.Equal("Frontend Dev", afterSecond.PrimaryRole); // preserved
    }

    [Fact]
    public async Task UpdateProfile_WithAnEmptySkillList_ClearsTheSkills()
    {
        // An EMPTY list is a real instruction ("clear them"), which is why it is not conflated with absent.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, member, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{member.Id}/profile",
            new { skills = new[] { "csharp" } });

        await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{member.Id}/profile",
            new { skills = Array.Empty<string>() });

        var stored = await LoadMemberAsync(scenario, workspace.Id, member.Id);
        Assert.Empty(stored.Skills);
    }

    [Fact]
    public async Task UpdateProfile_WithNoFieldsAtAll_IsAnIdempotentNoOp()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, member, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{member.Id}/profile",
            new { });

        // Re-sending nothing is a valid no-op, not a 400 (the "same role twice" precedent).
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var activity = await scenario.CountAsync<ActivityLog>(
            a => a.WorkspaceId == workspace.Id
                 && a.Action == ObserverActivityActions.MemberSkillsChanged);
        Assert.Equal(0, activity);
    }

    [Theory]
    [InlineData("csharp")]                 // ok
    [InlineData("C#")]                     // uppercase + symbol ⇒ invalid
    [InlineData("-leading")]               // must start alphanumeric
    [InlineData("has space")]              // no spaces
    [InlineData("")]
    public async Task UpdateProfile_ValidatesTheSkillSlugShape(string slug)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, member, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{member.Id}/profile",
            new { skills = new[] { slug } });

        // Only the first case is a usable slug.
        var expected = slug == "csharp" ? HttpStatusCode.NoContent : HttpStatusCode.BadRequest;
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProfile_WithTooManySkills_IsBadRequest()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, member, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var tooMany = Enumerable.Range(0, TaxonomyDefaults.MaxSkillsPerMember + 1)
            .Select(index => $"skill{index}")
            .ToArray();

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{member.Id}/profile",
            new { skills = tooMany });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProfile_WithAnUnknownPrimaryRole_IsBadRequest()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, member, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{member.Id}/profile",
            new { primaryRole = "Chief Vibe Officer" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProfile_RoleMatchingIsCaseInsensitiveAndReturnsTheCanonicalSpelling()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, member, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{member.Id}/profile",
            new { primaryRole = "backend dev" });

        var stored = await LoadMemberAsync(scenario, workspace.Id, member.Id);

        Assert.Equal("Backend Dev", stored.PrimaryRole);
    }

    [Theory]
    [InlineData(0.0)]     // zero would make the member absorb infinite work in the dispatching maths
    [InlineData(-1.0)]
    [InlineData(3.5)]     // above the documented ceiling
    public async Task UpdateProfile_WithAnOutOfRangeCapacityWeight_IsBadRequest(double weight)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, member, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{member.Id}/profile",
            new { capacityWeight = weight });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(51)]
    public async Task UpdateProfile_WithAnOutOfRangeWipLimit_IsBadRequest(int limit)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, member, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{member.Id}/profile",
            new { maxWipLimit = limit });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProfile_ByManager_IsForbidden()
    {
        // Admin only, deliberately the same gate as changing a role: repackaging the team is one act.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, member, workspace) = await SeedAsync(scenario);

        var manager = await scenario.CreateUserAsync("Quản lý");
        await using (var db = scenario.NewDbContext())
        {
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspace.Id,
                UserId = manager.Id,
                Role = WorkspaceRole.Manager,
            });
            await db.SaveChangesAsync();
        }

        using var client = await scenario.AsUserAsync(manager);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{member.Id}/profile",
            new { skills = new[] { "csharp" } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProfile_OnTheAiAgentRow_IsRefused()
    {
        // The workspace's virtual co-worker is not a person whose skills a human should be editing.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        // Reading the members list is what materialises the agent (Phase 7 §3.6).
        var members = await client.GetJsonAsync<List<MemberDto>>($"/api/workspaces/{workspace.Id}/members");
        var agent = members!.Single(m => m.MemberType == "ai_agent");

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{agent.UserId}/profile",
            new { skills = new[] { "csharp" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProfile_OnTheOwner_IsRefused()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{admin.Id}/profile",
            new { skills = new[] { "csharp" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProfile_OnAUserOutsideTheWorkspace_IsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, workspace) = await SeedAsync(scenario);
        var outsider = await scenario.CreateUserAsync("Người ngoài");
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{outsider.Id}/profile",
            new { skills = new[] { "csharp" } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProfile_ThenGetMembers_ReturnsTheStoredProfile()
    {
        // The read path is what the Jev dispatching context is built from, so the round-trip is the
        // assertion that matters: a value that is written but never read back would be invisible to the AI.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, member, workspace) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{member.Id}/profile",
            new
            {
                skills = new[] { "react", "typescript" },
                primaryRole = "Frontend Dev",
                capacityWeight = 0.75,
                maxWipLimit = 4,
            });

        var members = await client.GetJsonAsync<List<MemberDto>>($"/api/workspaces/{workspace.Id}/members");
        var row = members!.Single(m => m.UserId == member.Id);

        Assert.Equal(["react", "typescript"], row.Skills);
        Assert.Equal("Frontend Dev", row.PrimaryRole);
        Assert.Equal(0.75, row.CapacityWeight);
        Assert.Equal(4, row.MaxWipLimit);
    }

    private sealed record MemberDto
    {
        public Guid UserId { get; init; }
        public string MemberType { get; init; } = string.Empty;
        public List<string> Skills { get; init; } = [];
        public string PrimaryRole { get; init; } = string.Empty;
        public double CapacityWeight { get; init; }
        public int MaxWipLimit { get; init; }
    }
}
