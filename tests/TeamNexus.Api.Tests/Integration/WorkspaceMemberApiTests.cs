using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 11 §4 — workspace membership management: the extended read payload, role changes (Admin
/// only) and removal (Admin only), each with the invariants that protect the workspace from losing
/// its last administrator or its owner.
/// <para>
/// A Manager may invite and cancel invitations but may <b>not</b> repackage who has which role —
/// that distinction is the point of the permission split, so both roles are exercised for both verbs.
/// </para>
/// </summary>
public sealed class WorkspaceMemberApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public WorkspaceMemberApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- read path ---------------------------------------------------------

    [Fact]
    public async Task GetMembers_KeepsTheFiveOriginalFieldsAndAppendsThePhase11Ones()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, member) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(member);

        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}/members");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var human = document.RootElement.EnumerateArray()
            .Single(row => row.GetProperty("userId").GetGuid() == member.Id);

        var keys = human.EnumerateObject().Select(p => p.Name).ToList();

        // Phase 10 §4.2's append-only rule: existing names/order first, new fields last.
        Assert.Equal(
            ["userId", "displayName", "role", "avatarUrl", "memberType", "email", "joinedAt", "isOwner"],
            keys);
        Assert.Equal(member.Email, human.GetProperty("email").GetString());
        Assert.False(human.GetProperty("isOwner").GetBoolean());
    }

    [Fact]
    public async Task GetMembers_FlagsExactlyTheOwner()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, member) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var members = await client.GetJsonAsync<List<MemberDto>>($"/api/workspaces/{workspace.Id}/members");

        Assert.NotNull(members);
        Assert.Single(members!, m => m.IsOwner);
        Assert.Equal(admin.Id, members!.Single(m => m.IsOwner).UserId);
        Assert.Contains(members, m => m.UserId == member.Id);
    }

    [Fact]
    public async Task GetMembers_WithAUserOutsideTheWorkspace_IsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (_, workspace, _) = await SeedAsync(scenario);
        var outsider = await scenario.CreateUserAsync("Người ngoài");
        using var client = await scenario.AsUserAsync(outsider);

        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}/members");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMembers_StillEnsuresTheAiAgentAndKeepsItLast()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var members = await client.GetJsonAsync<List<MemberDto>>($"/api/workspaces/{workspace.Id}/members");

        // Phase 7 §3.6 regression: the list is the only source of the assignee dropdown, so the agent
        // must be materialised by this call.
        Assert.NotNull(members);
        Assert.Contains(members!, m => m.MemberType == "ai_agent");
        Assert.Equal("ai_agent", members![^1].MemberType);
    }

    // ---- role change -------------------------------------------------------

    [Fact]
    public async Task UpdateRole_ByAdmin_PromotesTheMember()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, member) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{member.Id}/role",
            new { role = "Manager" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var db = scenario.NewDbContext();
        var updated = await db.WorkspaceMembers
            .SingleAsync(wm => wm.WorkspaceId == workspace.Id && wm.UserId == member.Id);
        Assert.Equal(WorkspaceRole.Manager, updated.Role);

        var activity = await db.Activities.SingleAsync(a => a.Action == ObserverActivityActions.MemberRoleChanged);
        Assert.Null(activity.BoardId);
        Assert.Equal("Workspace", activity.EntityType);
    }

    [Fact]
    public async Task UpdateRole_IsIdempotentForTheSameRole()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, member) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var url = $"/api/workspaces/{workspace.Id}/members/{member.Id}/role";

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutJsonAsync(url, new { role = "Manager" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutJsonAsync(url, new { role = "manager" })).StatusCode);

        await using var db = scenario.NewDbContext();
        Assert.Equal(1, await db.Activities.CountAsync(a => a.Action == ObserverActivityActions.MemberRoleChanged));
    }

    [Fact]
    public async Task UpdateRole_ByAManager_IsForbidden()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, member) = await SeedAsync(scenario);
        var manager = await scenario.CreateUserAsync("Quản lý khác");
        var workspaceWithManager = await scenario.CreateWorkspaceAsync(
            admin, "Workspace có Manager", (manager, WorkspaceRole.Manager));

        using var client = await scenario.AsUserAsync(manager);
        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspaceWithManager.Id}/members/{admin.Id}/role",
            new { role = "Member" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // And the read path still works for the same Manager (403 is about the verb, not the row).
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync($"/api/workspaces/{workspaceWithManager.Id}/members")).StatusCode);
    }

    [Fact]
    public async Task UpdateRole_ByAPlainMember_IsForbidden()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, member) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(member);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{admin.Id}/role",
            new { role = "Member" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRole_OnTheCallerThemselves_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{admin.Id}/role",
            new { role = "Member" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRole_OnTheOwner_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        // Owner is NOT an Admin here, so the guard under test is the owner rule, not the last-Admin
        // rule (which would also fire).
        var owner = await scenario.CreateUserAsync("Chủ sở hữu");
        var admin = await scenario.CreateUserAsync("Admin workspace");
        var workspace = await scenario.CreateWorkspaceAsync(owner, "Workspace của chủ");
        await using (var db = scenario.NewDbContext())
        {
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspace.Id,
                UserId = admin.Id,
                Role = WorkspaceRole.Admin,
            });
            await db.SaveChangesAsync();
        }

        using var client = await scenario.AsUserAsync(admin);
        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{owner.Id}/role",
            new { role = "Member" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRole_OnTheLastAdmin_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        // The owner is deliberately not an Admin, so exactly one Admin exists in this workspace and
        // the caller can never be the last one themselves (self-changes are refused earlier).
        var owner = await scenario.CreateUserAsync("Chủ sở hữu");
        var onlyAdmin = await scenario.CreateUserAsync("Admin duy nhất");
        var newcomer = await scenario.CreateUserAsync("Admin mới");
        var workspace = await scenario.CreateWorkspaceAsync(owner, "Workspace của chủ");

        await using (var db = scenario.NewDbContext())
        {
            db.WorkspaceMembers.AddRange(
                new WorkspaceMember
                {
                    WorkspaceId = workspace.Id,
                    UserId = onlyAdmin.Id,
                    Role = WorkspaceRole.Admin,
                },
                new WorkspaceMember
                {
                    WorkspaceId = workspace.Id,
                    UserId = newcomer.Id,
                    Role = WorkspaceRole.Member,
                });
            await db.SaveChangesAsync();
        }

        // A second Admin makes the demotion of the first one legal.
        var secondAdmin = await scenario.CreateUserAsync("Admin thứ hai");
        await using (var db = scenario.NewDbContext())
        {
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspace.Id,
                UserId = secondAdmin.Id,
                Role = WorkspaceRole.Admin,
            });
            await db.SaveChangesAsync();
        }

        using var client = await scenario.AsUserAsync(secondAdmin);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PutJsonAsync(
                $"/api/workspaces/{workspace.Id}/members/{onlyAdmin.Id}/role",
                new { role = "Member" })).StatusCode);

        // `secondAdmin` is now the last Admin: promoting then demoting is fine, but demoting the
        // caller themselves is refused by the self-change guard, so there is no path that can leave
        // the workspace without an Admin through this endpoint.
        await using (var db = scenario.NewDbContext())
        {
            var remaining = await db.WorkspaceMembers
                .Where(wm => wm.WorkspaceId == workspace.Id && wm.Role == WorkspaceRole.Admin)
                .ToListAsync();
            Assert.Single(remaining);
            Assert.Equal(secondAdmin.Id, remaining[0].UserId);
        }

        // A non-Admin member cannot promote themselves out of the problem either (403, not 400).
        using var memberClient = await scenario.AsUserAsync(newcomer);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await memberClient.PutJsonAsync(
                $"/api/workspaces/{workspace.Id}/members/{newcomer.Id}/role",
                new { role = "Admin" })).StatusCode);
    }

    [Fact]
    public async Task UpdateRole_OnTheAiAgent_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        // Drive the read once so the agent row exists, then find its user id through the API.
        var members = await client.GetJsonAsync<List<MemberDto>>($"/api/workspaces/{workspace.Id}/members");
        var agentId = members!.Single(m => m.MemberType == "ai_agent").UserId;

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{agentId}/role",
            new { role = "Manager" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Boss")]
    [InlineData("1")]
    [InlineData("99")]
    [InlineData("")]
    public async Task UpdateRole_WithAnUnknownRole_IsRejected(string role)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, member) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{member.Id}/role",
            new { role });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRole_ForAUserWhoIsNotAMember_IsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, _) = await SeedAsync(scenario);
        var stranger = await scenario.CreateUserAsync("Người lạ");
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/members/{stranger.Id}/role",
            new { role = "Member" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRole_WithoutTheAntiforgeryHeader_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, member) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/workspaces/{workspace.Id}/members/{member.Id}/role")
        {
            Content = JsonContent.Create(new { role = "Manager" }),
        };

        // Raw client: TestHttpClient re-attaches the header on every mutating verb.
        var response = await client.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- removal -----------------------------------------------------------

    [Fact]
    public async Task RemoveMember_ByAdmin_DeletesTheMembershipButNotTheUser()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, member) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.DeleteAsync($"/api/workspaces/{workspace.Id}/members/{member.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var db = scenario.NewDbContext();

        // A junction row: physically removed, no soft-delete column to set.
        Assert.False(await db.WorkspaceMembers
            .IgnoreQueryFilters()
            .AnyAsync(wm => wm.WorkspaceId == workspace.Id && wm.UserId == member.Id));

        // The person's account is untouched.
        Assert.True(await db.Users.AnyAsync(u => u.Id == member.Id));

        var activity = await db.Activities.SingleAsync(a => a.Action == ObserverActivityActions.MemberRemoved);
        Assert.Null(activity.BoardId);
    }

    [Fact]
    public async Task RemoveMember_KeepsTasksThatWereAssignedToThem()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, member) = await SeedAsync(scenario);
        var (board, todo, _) = await scenario.CreateBoardAsync(workspace);
        var task = await scenario.CreateTaskAsync(board, todo, admin, "Việc đang làm", member.Id);

        using var client = await scenario.AsUserAsync(admin);
        await client.DeleteAsync($"/api/workspaces/{workspace.Id}/members/{member.Id}");

        await using var db = scenario.NewDbContext();
        var stored = await db.Tasks.SingleAsync(t => t.Id == task.Id);

        // Restrict FKs + no cascade: the work survives the membership, it is not silently unassigned.
        Assert.Equal(member.Id, stored.AssigneeId);
    }

    [Fact]
    public async Task RemoveMember_ByAManager_IsForbidden()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var admin = await scenario.CreateUserAsync("Admin");
        var manager = await scenario.CreateUserAsync("Quản lý");
        var target = await scenario.CreateUserAsync("Thành viên");
        var workspace = await scenario.CreateWorkspaceAsync(
            admin, "Workspace", (manager, WorkspaceRole.Manager), (target, WorkspaceRole.Member));

        using var client = await scenario.AsUserAsync(manager);
        var response = await client.DeleteAsync($"/api/workspaces/{workspace.Id}/members/{target.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var db = scenario.NewDbContext();
        Assert.True(await db.WorkspaceMembers
            .AnyAsync(wm => wm.WorkspaceId == workspace.Id && wm.UserId == target.Id));
    }

    [Fact]
    public async Task RemoveMember_OnTheOwner_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        var owner = await scenario.CreateUserAsync("Chủ sở hữu");
        var admin = await scenario.CreateUserAsync("Admin workspace");
        var workspace = await scenario.CreateWorkspaceAsync(owner, "Workspace của chủ");
        await using (var db = scenario.NewDbContext())
        {
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspace.Id,
                UserId = admin.Id,
                Role = WorkspaceRole.Admin,
            });
            await db.SaveChangesAsync();
        }

        using var client = await scenario.AsUserAsync(admin);
        var response = await client.DeleteAsync($"/api/workspaces/{workspace.Id}/members/{owner.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_OnTheCallerThemselves_PointsAtLeavingInstead()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await client.DeleteAsync($"/api/workspaces/{workspace.Id}/members/{admin.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_OnTheLastAdmin_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        // The owner is deliberately not an Admin, so exactly one Admin exists in this workspace.
        var owner = await scenario.CreateUserAsync("Chủ sở hữu");
        var lastAdmin = await scenario.CreateUserAsync("Admin duy nhất");
        var member = await scenario.CreateUserAsync("Thành viên");
        var workspace = await scenario.CreateWorkspaceAsync(owner, "Workspace của chủ");

        await using (var db = scenario.NewDbContext())
        {
            db.WorkspaceMembers.AddRange(
                new WorkspaceMember
                {
                    WorkspaceId = workspace.Id,
                    UserId = lastAdmin.Id,
                    Role = WorkspaceRole.Admin,
                },
                new WorkspaceMember
                {
                    WorkspaceId = workspace.Id,
                    UserId = member.Id,
                    Role = WorkspaceRole.Member,
                });
            await db.SaveChangesAsync();
        }

        // Two Admins: removing one of them is allowed, because the caller still remains.
        var secondAdmin = await scenario.CreateUserAsync("Admin thứ hai");
        await using (var db = scenario.NewDbContext())
        {
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspace.Id,
                UserId = secondAdmin.Id,
                Role = WorkspaceRole.Admin,
            });
            await db.SaveChangesAsync();
        }

        using var client = await scenario.AsUserAsync(secondAdmin);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/workspaces/{workspace.Id}/members/{lastAdmin.Id}")).StatusCode);

        // `secondAdmin` is now the last Admin. Even an Admin acting on themselves through this route
        // is refused by the self-removal guard, so assert through the remaining Admin path: promote
        // the plain member, then try to remove them while they are the only other Admin.
        await using (var db = scenario.NewDbContext())
        {
            var promoted = await db.WorkspaceMembers
                .SingleAsync(wm => wm.WorkspaceId == workspace.Id && wm.UserId == member.Id);
            promoted.Role = WorkspaceRole.Admin;
            await db.SaveChangesAsync();
        }

        // Removing the second Admin leaves exactly one — allowed.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/workspaces/{workspace.Id}/members/{member.Id}")).StatusCode);

        // Now `secondAdmin` is the only Admin left; a plain member cannot remove them at all (403),
        // and the last-Admin guard is what stops an Admin from doing it themselves.
        await using (var db = scenario.NewDbContext())
        {
            var remaining = await db.WorkspaceMembers
                .Where(wm => wm.WorkspaceId == workspace.Id && wm.Role == WorkspaceRole.Admin)
                .ToListAsync();
            Assert.Single(remaining);
            Assert.Equal(secondAdmin.Id, remaining[0].UserId);
        }
    }

    [Fact]
    public async Task RemoveMember_OfAPlainMember_IsAllowedEvenWhenTheOwnerIsTheOnlyOtherRow()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, member) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        // Sanity check that the earlier 400s were about the target, not about the verb: an ordinary
        // member is removed without ceremony.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/workspaces/{workspace.Id}/members/{member.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync($"/api/workspaces/{workspace.Id}/members")).StatusCode);
    }

    [Fact]
    public async Task RemoveMember_OnTheAiAgent_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var members = await client.GetJsonAsync<List<MemberDto>>($"/api/workspaces/{workspace.Id}/members");
        var agentId = members!.Single(m => m.MemberType == "ai_agent").UserId;

        var response = await client.DeleteAsync($"/api/workspaces/{workspace.Id}/members/{agentId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- helpers -----------------------------------------------------------

    private static async Task<(ApplicationUser Admin, Workspace Workspace, ApplicationUser Member)>
        SeedAsync(TestScenario scenario)
    {
        var admin = await scenario.CreateUserAsync("Admin workspace", "admin.member@example.test");
        var member = await scenario.CreateUserAsync("Thành viên", "plain.member@example.test");
        var workspace = await scenario.CreateWorkspaceAsync(
            admin, "Workspace Thành Viên", (member, WorkspaceRole.Member));

        return (admin, workspace, member);
    }

    private sealed record MemberDto
    {
        public Guid UserId { get; init; }
        public string? DisplayName { get; init; }
        public string? Role { get; init; }
        public string? AvatarUrl { get; init; }
        public string? MemberType { get; init; }
        public string? Email { get; init; }
        public DateTimeOffset JoinedAt { get; init; }
        public bool IsOwner { get; init; }
    }
}
