using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 11 §5 — the signed-in user's own profile: read/update the two editable fields, list the
/// workspaces they belong to, and leave one.
/// <para>
/// Two properties get dedicated tests because they are easy to regress:
/// <c>GET /api/users/me/workspaces</c> must stay <b>read-only</b> (unlike <c>GET /api/workspaces</c>,
/// which creates a default workspace), and <c>PUT /api/users/me</c> must keep
/// <c>GET /api/auth/me</c> — the payload the frontend auth store bootstraps from — in sync.
/// </para>
/// </summary>
public sealed class ProfileApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public ProfileApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- read --------------------------------------------------------------

    [Fact]
    public async Task Get_ReturnsTheCallersOwnProfile()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Người dùng", "profile.get@example.test");
        using var client = await scenario.AsUserAsync(user);

        var profile = await client.GetJsonAsync<ProfileDto>("/api/users/me");

        Assert.NotNull(profile);
        Assert.Equal(user.Id, profile!.Id);
        Assert.Equal("profile.get@example.test", profile.Email);
        Assert.Equal("Người dùng", profile.DisplayName);
        Assert.Null(profile.AvatarUrl);
    }

    [Fact]
    public async Task Get_WithoutSigningIn_IsUnauthorized()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        using var anonymous = await scenario.AnonymousAsync();

        var response = await anonymous.GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- update ------------------------------------------------------------

    [Fact]
    public async Task Update_ChangesTheFieldsAndKeepsAuthMeInSync()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Tên cũ", "profile.update@example.test");
        using var client = await scenario.AsUserAsync(user);

        var response = await client.PutJsonAsync(
            "/api/users/me",
            new { displayName = "  Tên mới  ", avatarUrl = "https://cdn.example.com/me.png" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<ProfileDto>();
        Assert.Equal("Tên mới", updated!.DisplayName);
        Assert.Equal("https://cdn.example.com/me.png", updated.AvatarUrl);

        // The frontend auth store reads /api/auth/me on every load; the two must not diverge.
        using var document = JsonDocument.Parse(await client.Http.GetStringAsync("/api/auth/me"));
        Assert.Equal("Tên mới", document.RootElement.GetProperty("displayName").GetString());
        Assert.Equal(
            "https://cdn.example.com/me.png",
            document.RootElement.GetProperty("avatarUrl").GetString());
    }

    [Fact]
    public async Task Update_WithABlankAvatar_RemovesIt()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Có ảnh", "profile.clear@example.test");
        using var client = await scenario.AsUserAsync(user);

        await client.PutJsonAsync(
            "/api/users/me",
            new { displayName = "Có ảnh", avatarUrl = "https://cdn.example.com/old.png" });

        var cleared = await (await client.PutJsonAsync(
            "/api/users/me",
            new { displayName = "Có ảnh", avatarUrl = "   " })).Content.ReadFromJsonAsync<ProfileDto>();

        Assert.Null(cleared!.AvatarUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Update_WithABlankDisplayName_IsRejected(string displayName)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Tên gốc");
        using var client = await scenario.AsUserAsync(user);

        var response = await client.PutJsonAsync("/api/users/me", new { displayName, avatarUrl = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithATooLongDisplayName_IsRejectedButTheBoundaryIsAccepted()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Tên gốc");
        using var client = await scenario.AsUserAsync(user);

        var tooLong = await client.PutJsonAsync(
            "/api/users/me",
            new { displayName = new string('x', 121), avatarUrl = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        var boundary = await client.PutJsonAsync(
            "/api/users/me",
            new { displayName = new string('x', 120), avatarUrl = (string?)null });
        Assert.Equal(HttpStatusCode.OK, boundary.StatusCode);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("file:///C:/secrets.txt")]
    [InlineData("//evil.example.com/a.png")]
    [InlineData("vbscript:msgbox(1)")]
    public async Task Update_WithAnUnsafeAvatarUrl_IsRejected(string avatarUrl)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Tên gốc");
        using var client = await scenario.AsUserAsync(user);

        var response = await client.PutJsonAsync("/api/users/me", new { displayName = "Tên gốc", avatarUrl });

        // Reported, never silently dropped: the user must learn the value was not stored.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var db = scenario.NewDbContext();
        Assert.Null((await db.Users.SingleAsync(u => u.Id == user.Id)).AvatarUrl);
    }

    [Fact]
    public async Task Update_WithoutTheAntiforgeryHeader_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Tên gốc");
        using var client = await scenario.AsUserAsync(user);

        var request = new HttpRequestMessage(HttpMethod.Put, "/api/users/me")
        {
            Content = JsonContent.Create(new { displayName = "Tên mới", avatarUrl = (string?)null }),
        };

        var response = await client.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- my workspaces -----------------------------------------------------

    [Fact]
    public async Task MyWorkspaces_ForAUserWithNone_IsEmptyAndCreatesNothing()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Người mới");
        using var client = await scenario.AsUserAsync(user);

        var list = await client.GetJsonAsync<List<MyWorkspaceDto>>("/api/users/me/workspaces");

        Assert.NotNull(list);
        Assert.Empty(list!);

        // The read-only property: GET /api/workspaces would have created a default workspace here
        // (a side effect the dashboard depends on and the profile page must not trigger).
        await using var db = scenario.NewDbContext();
        Assert.Equal(0, await db.Workspaces.CountAsync());
        Assert.Equal(0, await db.WorkspaceMembers.CountAsync());
    }

    [Fact]
    public async Task MyWorkspaces_ListsEveryMembershipWithItsRole()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Người dùng");
        var asOwner = await scenario.CreateWorkspaceAsync(user, "Workspace của tôi");
        var other = await scenario.CreateUserAsync("Người khác");
        await scenario.CreateWorkspaceAsync(other, "Workspace A", (user, WorkspaceRole.Manager));
        await scenario.CreateWorkspaceAsync(other, "Workspace B", (user, WorkspaceRole.Member));

        using var client = await scenario.AsUserAsync(user);
        var list = await client.GetJsonAsync<List<MyWorkspaceDto>>("/api/users/me/workspaces");

        Assert.NotNull(list);
        Assert.Equal(3, list!.Count);

        var own = list.Single(w => w.Id == asOwner.Id);
        Assert.True(own.IsOwner);
        Assert.Equal("Admin", own.Role);

        Assert.Equal("Manager", list.Single(w => w.Name == "Workspace A").Role);
        Assert.False(list.Single(w => w.Name == "Workspace A").IsOwner);
    }

    [Fact]
    public async Task MyWorkspaces_DoesNotLeakASoftDeletedWorkspace()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Người dùng");
        var workspace = await scenario.CreateWorkspaceAsync(user, "Workspace sắp xoá");

        await using (var db = scenario.NewDbContext())
        {
            db.Workspaces.Update(workspace);
            workspace.DeletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        using var client = await scenario.AsUserAsync(user);
        var list = await client.GetJsonAsync<List<MyWorkspaceDto>>("/api/users/me/workspaces");

        Assert.NotNull(list);
        Assert.Empty(list!);
    }

    // ---- leaving -----------------------------------------------------------

    [Fact]
    public async Task Leave_RemovesTheMembershipButKeepsTheAccount()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var owner = await scenario.CreateUserAsync("Chủ sở hữu");
        var leaver = await scenario.CreateUserAsync("Người rời", "profile.leave@example.test");
        var workspace = await scenario.CreateWorkspaceAsync(
            owner, "Workspace", (leaver, WorkspaceRole.Member));

        using var client = await scenario.AsUserAsync(leaver);
        var response = await client.DeleteAsync($"/api/users/me/workspaces/{workspace.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var db = scenario.NewDbContext();
        Assert.False(await db.WorkspaceMembers
            .IgnoreQueryFilters()
            .AnyAsync(wm => wm.WorkspaceId == workspace.Id && wm.UserId == leaver.Id));
        Assert.True(await db.Users.AnyAsync(u => u.Id == leaver.Id));

        var activity = await db.Activities.SingleAsync(a => a.Action == ObserverActivityActions.MemberLeft);
        Assert.Null(activity.BoardId);
        Assert.Equal("Workspace", activity.EntityType);
    }

    [Fact]
    public async Task Leave_AsTheOwner_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var owner = await scenario.CreateUserAsync("Chủ sở hữu");
        var workspace = await scenario.CreateWorkspaceAsync(owner, "Workspace của tôi");

        using var client = await scenario.AsUserAsync(owner);
        var response = await client.DeleteAsync($"/api/users/me/workspaces/{workspace.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var db = scenario.NewDbContext();
        Assert.True(await db.WorkspaceMembers
            .AnyAsync(wm => wm.WorkspaceId == workspace.Id && wm.UserId == owner.Id));
    }

    [Fact]
    public async Task Leave_AsTheLastAdmin_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        // Seeded directly with the OWNER AS A PLAIN MEMBER: CreateWorkspaceAsync always makes the
        // owner an Admin, which would keep a second Admin alive and make the last-Admin guard
        // unreachable. The guard's real shape is a workspace whose owner is not an Admin.
        var owner = await scenario.CreateUserAsync("Chủ sở hữu");
        var admin = await scenario.CreateUserAsync("Admin duy nhất");
        var workspace = await SeedWorkspaceWithMembersAsync(
            scenario,
            owner,
            (owner, WorkspaceRole.Member),
            (admin, WorkspaceRole.Admin));

        using var client = await scenario.AsUserAsync(admin);
        var response = await client.DeleteAsync($"/api/users/me/workspaces/{workspace.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // With a second Admin in place the same call succeeds.
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

        using var retry = await scenario.AsUserAsync(admin);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await retry.DeleteAsync($"/api/users/me/workspaces/{workspace.Id}")).StatusCode);
    }

    [Fact]
    public async Task Leave_AWorkspaceTheCallerIsNotIn_IsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var owner = await scenario.CreateUserAsync("Chủ sở hữu");
        var workspace = await scenario.CreateWorkspaceAsync(owner, "Workspace");
        var outsider = await scenario.CreateUserAsync("Người ngoài");

        using var client = await scenario.AsUserAsync(outsider);
        var response = await client.DeleteAsync($"/api/users/me/workspaces/{workspace.Id}");

        // 404, not 403: the caller must not learn that the workspace exists.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Leave_Twice_IsNotFoundTheSecondTime()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var owner = await scenario.CreateUserAsync("Chủ sở hữu");
        var leaver = await scenario.CreateUserAsync("Người rời");
        var workspace = await scenario.CreateWorkspaceAsync(
            owner, "Workspace", (leaver, WorkspaceRole.Member));

        using var client = await scenario.AsUserAsync(leaver);
        var url = $"/api/users/me/workspaces/{workspace.Id}";

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(url)).StatusCode);
    }

    // ---- helpers -----------------------------------------------------------

    /// <summary>
    /// Writes the workspace and its memberships straight to the database, so a suite can control the
    /// owner's role.
    /// <para>
    /// <see cref="TestScenario.CreateWorkspaceAsync"/> always makes the owner an Admin — correct for
    /// the production flow, but it makes the "last Admin" guard unreachable, because there is always
    /// a second Admin. The guard's real shape is a workspace whose owner is NOT an Admin, which only
    /// a direct insert can reproduce.
    /// </para>
    /// </summary>
    private static async Task<Workspace> SeedWorkspaceWithMembersAsync(
        TestScenario scenario,
        ApplicationUser owner,
        params (ApplicationUser User, WorkspaceRole Role)[] members)
    {
        await using var db = scenario.NewDbContext();

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Workspace kiểm thử",
            OwnerId = owner.Id,
        };

        db.Workspaces.Add(workspace);

        foreach (var (user, role) in members)
        {
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspace.Id,
                UserId = user.Id,
                Role = role,
            });
        }

        await db.SaveChangesAsync();
        return workspace;
    }

    private sealed record ProfileDto
    {
        public Guid Id { get; init; }
        public string? Email { get; init; }
        public string? DisplayName { get; init; }
        public string? AvatarUrl { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    private sealed record MyWorkspaceDto
    {
        public Guid Id { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? Role { get; init; }
        public Guid OwnerId { get; init; }
        public bool IsOwner { get; init; }
    }
}
