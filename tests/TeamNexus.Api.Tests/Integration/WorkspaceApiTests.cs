using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 10 §2 — the workspace contract: the read path moved out of <c>Program.cs</c>, the
/// "Workspace Settings" mutations (rename / transfer ownership / delete) and the activity feed
/// (keyset pagination over <c>activity_logs</c>).
/// <para>
/// The read path is a refactor of Giai đoạn 1 code, so the first tests are deliberately about
/// <b>not regressing</b>: the previous payload had four fields
/// (<c>id</c>, <c>name</c>, <c>description</c>, <c>role</c>) that the frontend parses by name, and
/// the lazy "create my default workspace" side effect that <c>DashboardPage</c> depends on.
/// </para>
/// </summary>
public sealed class WorkspaceApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public WorkspaceApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- the read path that used to live inline in Program.cs ---------------

    [Fact]
    public async Task GetWorkspaces_ForAUserWithNone_CreatesTheDefaultWorkspace()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        // A user who is not a member of anything: this is where the old inline handler created a
        // default workspace so the dashboard always has somewhere to land.
        var user = await scenario.CreateUserAsync("Người mới");
        using var client = await scenario.AsUserAsync(user);

        var workspaces = await client.GetJsonAsync<List<WorkspaceSummaryDto>>("/api/workspaces");

        Assert.NotNull(workspaces);
        var created = Assert.Single(workspaces!);
        Assert.Equal("Không Gian Làm Việc Chính", created.Name);
        Assert.Equal("Workspace mặc định để quản lý bảng Kanban", created.Description);
        Assert.Equal("Admin", created.Role);
        Assert.Equal(user.Id, created.OwnerId);
        Assert.True(created.IsOwner);
    }

    [Fact]
    public async Task GetWorkspaces_CalledTwice_DoesNotCreateASecondDefaultWorkspace()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Người mới");
        using var client = await scenario.AsUserAsync(user);

        await client.GetJsonAsync<List<WorkspaceSummaryDto>>("/api/workspaces");
        var second = await client.GetJsonAsync<List<WorkspaceSummaryDto>>("/api/workspaces");

        Assert.NotNull(second);
        Assert.Single(second!);
        Assert.Equal(1, await scenario.CountAsync<WorkspaceMember>(wm => wm.UserId == user.Id));
    }

    /// <summary>
    /// Locks the wire contract: the two Phase 10 fields are appended, and the four original keys keep
    /// their names. Three frontend call sites read this payload.
    /// </summary>
    [Fact]
    public async Task GetWorkspaces_KeepsTheOriginalPayloadShapeAndAppendsOwnerFields()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (owner, workspace, _, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(owner);

        var response = await client.GetAsync("/api/workspaces");
        var json = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(json);
        var row = document.RootElement.EnumerateArray().Single();
        var keys = row.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Equal(
            ["id", "name", "description", "role", "ownerId", "isOwner"],
            keys);

        Assert.Equal(workspace.Id, row.GetProperty("id").GetGuid());
        Assert.Equal("Admin", row.GetProperty("role").GetString());
        Assert.True(row.GetProperty("isOwner").GetBoolean());
    }

    [Fact]
    public async Task GetWorkspaces_RequiresAuthentication()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        using var anonymous = await scenario.AnonymousAsync();

        var response = await anonymous.GetAsync("/api/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- workspace detail ---------------------------------------------------

    [Fact]
    public async Task GetWorkspace_ForAMember_ReturnsCountsAndOwnerName()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        var owner = await scenario.CreateUserAsync("Chủ sở hữu");
        var member = await scenario.CreateUserAsync("Thành viên");
        var workspace = await scenario.CreateWorkspaceAsync(
            owner, "Workspace chi tiết", (member, WorkspaceRole.Member));
        await scenario.CreateBoardAsync(workspace);

        using var client = await scenario.AsUserAsync(member);
        var detail = await client.GetJsonAsync<WorkspaceDetailDto>($"/api/workspaces/{workspace.Id}");

        Assert.NotNull(detail);
        Assert.Equal("Workspace chi tiết", detail!.Name);
        Assert.Equal(owner.Id, detail.OwnerId);
        Assert.Equal("Chủ sở hữu", detail.OwnerDisplayName);
        Assert.Equal(2, detail.MemberCount);
        Assert.Equal(1, detail.BoardCount);
        Assert.Equal("Member", detail.CurrentUserRole);
    }

    [Fact]
    public async Task GetWorkspace_ForANonMember_Returns404()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (_, workspace, _, _, _) = await SeedAsync(scenario);

        var outsider = await scenario.CreateUserAsync("Người ngoài");
        using var client = await scenario.AsUserAsync(outsider);

        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}");

        // 404 rather than 403: the caller must not be able to probe for workspace existence.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetWorkspace_AfterASoftDelete_Returns404()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (owner, workspace, _, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(owner);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/workspaces/{workspace.Id}")).StatusCode);

        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- rename (PUT) -------------------------------------------------------

    [Fact]
    public async Task UpdateWorkspace_AsManager_RenamesAndRecordsActivity()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        var owner = await scenario.CreateUserAsync("Chủ sở hữu");
        var manager = await scenario.CreateUserAsync("Quản lý");
        var workspace = await scenario.CreateWorkspaceAsync(
            owner, "Tên cũ", (manager, WorkspaceRole.Manager));

        using var client = await scenario.AsUserAsync(manager);
        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}",
            new { name = "Tên mới", description = "Mô tả mới" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await client.GetJsonAsync<WorkspaceDetailDto>($"/api/workspaces/{workspace.Id}");
        Assert.Equal("Tên mới", detail!.Name);
        Assert.Equal("Mô tả mới", detail.Description);

        var log = Assert.Single(
            await ReadActivityAsync(scenario, workspace.Id, ObserverActivityActions.WorkspaceUpdated));
        Assert.Equal(ObserverEntityTypes.Workspace, log.EntityType);
        Assert.Equal(workspace.Id, log.EntityId);
        Assert.Equal(manager.Id, log.UserId);
        Assert.Null(log.BoardId);   // workspace-level event
    }

    [Fact]
    public async Task UpdateWorkspace_AsAMember_Returns403()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        var owner = await scenario.CreateUserAsync("Chủ sở hữu");
        var member = await scenario.CreateUserAsync("Thành viên");
        var workspace = await scenario.CreateWorkspaceAsync(
            owner, "Workspace", (member, WorkspaceRole.Member));

        using var client = await scenario.AsUserAsync(member);
        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}",
            new { name = "Đổi trộm", description = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var unchanged = await scenario.FindAsync<Workspace>(workspace.Id);
        Assert.Equal("Workspace", unchanged!.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateWorkspace_WithABlankName_Returns400(string name)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (owner, workspace, _, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(owner);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}",
            new { name, description = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateWorkspace_NameLengthBoundaryIs120()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (owner, workspace, _, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(owner);

        var exactly120 = new string('a', 120);
        var accepted = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}",
            new { name = exactly120, description = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);

        var rejected = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}",
            new { name = new string('a', 121), description = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task UpdateWorkspace_WithoutTheAntiforgeryHeader_Returns403()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (owner, workspace, _, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(owner);

        // Send through the raw HttpClient so the test wrapper's automatic X-XSRF-TOKEN header is
        // bypassed — the mirror image of httpClient.ts, which always echoes the cookie value.
        // (client.PutJsonAsync would re-attach a valid token and defeat the assertion; same pattern
        // as AuthApiTests.Refresh_WithoutTheXsrfHeader_IsRejected.)
        client.RemoveAntiforgeryHeader();

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/workspaces/{workspace.Id}")
        {
            Content = JsonContent.Create(new { name = "Không có CSRF", description = (string?)null }),
        };

        var response = await client.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var unchanged = await scenario.FindAsync<Workspace>(workspace.Id);
        Assert.NotEqual("Không có CSRF", unchanged!.Name);
    }

    // ---- transfer ownership (PUT /owner) ------------------------------------

    [Fact]
    public async Task TransferOwnership_PromotesTheNewOwnerAndKeepsTheOldOwnersRole()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        var owner = await scenario.CreateUserAsync("Chủ cũ");
        var member = await scenario.CreateUserAsync("Chủ mới");
        var workspace = await scenario.CreateWorkspaceAsync(
            owner, "Workspace chuyển quyền", (member, WorkspaceRole.Member));

        using var client = await scenario.AsUserAsync(owner);
        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/owner",
            new { newOwnerId = member.Id });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var after = await scenario.FindAsync<Workspace>(workspace.Id);
        Assert.Equal(member.Id, after!.OwnerId);

        // The new owner must be able to administer the workspace, so they are promoted …
        var newOwnerMembership = await FindMembershipAsync(scenario, workspace.Id, member.Id);
        Assert.Equal(WorkspaceRole.Admin, newOwnerMembership!.Role);

        // … and the previous owner is NOT silently demoted (Phase 10 D3).
        var oldOwnerMembership = await FindMembershipAsync(scenario, workspace.Id, owner.Id);
        Assert.Equal(WorkspaceRole.Admin, oldOwnerMembership!.Role);
    }

    [Fact]
    public async Task TransferOwnership_ToTheCurrentOwner_Returns400()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (owner, workspace, _, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(owner);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/owner",
            new { newOwnerId = owner.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TransferOwnership_ToANonMember_Returns400()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (owner, workspace, _, _, _) = await SeedAsync(scenario);
        var outsider = await scenario.CreateUserAsync("Người ngoài");
        using var client = await scenario.AsUserAsync(owner);

        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/owner",
            new { newOwnerId = outsider.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TransferOwnership_ToTheAiAgent_Returns400()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (owner, workspace, _, _, _) = await SeedAsync(scenario);

        // The AI Agent is a real users row + workspace_members row (Phase 7 D1), so without an
        // explicit guard it would pass the "is a member" check and could end up owning a workspace.
        var agent = await CreateAiAgentAsync(scenario, workspace, "TeamNexus Agent");

        using var client = await scenario.AsUserAsync(owner);
        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/owner",
            new { newOwnerId = agent.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var unchanged = await scenario.FindAsync<Workspace>(workspace.Id);
        Assert.Equal(owner.Id, unchanged!.OwnerId);
    }

    [Fact]
    public async Task TransferOwnership_ByAManagerWhoIsNotTheOwner_Returns403()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        var owner = await scenario.CreateUserAsync("Chủ sở hữu");
        var manager = await scenario.CreateUserAsync("Quản lý");
        var member = await scenario.CreateUserAsync("Thành viên");
        var workspace = await scenario.CreateWorkspaceAsync(
            owner,
            "Workspace",
            (manager, WorkspaceRole.Manager),
            (member, WorkspaceRole.Member));

        using var client = await scenario.AsUserAsync(manager);
        var response = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/owner",
            new { newOwnerId = member.Id });

        // Manager is enough to rename a workspace but NOT to hand it away.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var unchanged = await scenario.FindAsync<Workspace>(workspace.Id);
        Assert.Equal(owner.Id, unchanged!.OwnerId);
    }

    [Fact]
    public async Task TransferOwnership_RecordsActivity()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        var owner = await scenario.CreateUserAsync("Chủ cũ");
        var member = await scenario.CreateUserAsync("Chủ mới");
        var workspace = await scenario.CreateWorkspaceAsync(
            owner, "Workspace", (member, WorkspaceRole.Member));

        using var client = await scenario.AsUserAsync(owner);
        await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/owner",
            new { newOwnerId = member.Id });

        var log = Assert.Single(
            await ReadActivityAsync(scenario, workspace.Id, ObserverActivityActions.WorkspaceOwnerTransferred));

        Assert.Equal(ObserverEntityTypes.Workspace, log.EntityType);
        Assert.Null(log.BoardId);
        Assert.NotNull(log.Payload);
        Assert.Contains(member.Id.ToString("D"), log.Payload!, StringComparison.OrdinalIgnoreCase);
    }

    // ---- delete -------------------------------------------------------------

    [Fact]
    public async Task DeleteWorkspace_SoftDeletesItAndKeepsTheRows()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (owner, workspace, _, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(owner);

        var response = await client.DeleteAsync($"/api/workspaces/{workspace.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Gone from every read …
        var listed = await client.GetJsonAsync<List<WorkspaceSummaryDto>>("/api/workspaces");
        Assert.DoesNotContain(listed!, w => w.Id == workspace.Id);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/workspaces/{workspace.Id}")).StatusCode);

        // … but soft-deleted, not hard-deleted: the row and its memberships survive.
        var softDeleted = await scenario.FindIncludingSoftDeletedAsync<Workspace>(workspace.Id);
        Assert.NotNull(softDeleted);
        Assert.NotNull(softDeleted!.DeletedAt);

        // IgnoreQueryFilters, because WorkspaceMemberConfiguration hides a membership whose
        // workspace is soft-deleted — the count through the normal filter would be 0, which is
        // precisely the visibility behaviour under test, not the row's existence.
        await using var db = scenario.NewDbContext();
        var survivingMemberships = await db.WorkspaceMembers
            .IgnoreQueryFilters()
            .CountAsync(wm => wm.WorkspaceId == workspace.Id);
        Assert.Equal(1, survivingMemberships);
    }

    /// <summary>
    /// The delete is the one mutation whose activity row cannot be read back through the endpoint
    /// (the workspace is hidden immediately), so it is asserted straight from the database.
    /// </summary>
    [Fact]
    public async Task DeleteWorkspace_StillWritesItsActivityRow()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (owner, workspace, _, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(owner);

        await client.DeleteAsync($"/api/workspaces/{workspace.Id}");

        var log = Assert.Single(
            await ReadActivityAsync(scenario, workspace.Id, ObserverActivityActions.WorkspaceDeleted));
        Assert.Equal(ObserverEntityTypes.Workspace, log.EntityType);
        Assert.Null(log.BoardId);
    }

    [Fact]
    public async Task DeleteWorkspace_ByAManagerWhoIsNotTheOwner_Returns403()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        var owner = await scenario.CreateUserAsync("Chủ sở hữu");
        var manager = await scenario.CreateUserAsync("Quản lý");
        var workspace = await scenario.CreateWorkspaceAsync(
            owner, "Workspace", (manager, WorkspaceRole.Manager));

        using var client = await scenario.AsUserAsync(manager);
        var response = await client.DeleteAsync($"/api/workspaces/{workspace.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null((await scenario.FindAsync<Workspace>(workspace.Id))!.DeletedAt);
    }

    // ---- activity feed ------------------------------------------------------

    [Fact]
    public async Task Activity_ForAMember_Returns403()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        var owner = await scenario.CreateUserAsync("Chủ sở hữu");
        var member = await scenario.CreateUserAsync("Thành viên");
        var workspace = await scenario.CreateWorkspaceAsync(
            owner, "Workspace", (member, WorkspaceRole.Member));

        using var client = await scenario.AsUserAsync(member);
        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}/activity");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Activity_ListsTheWorkspaceHistoryNewestFirst()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (owner, workspace, board, todo, _) = await SeedAsync(scenario);

        using var client = await scenario.AsUserAsync(owner);

        // All three writes go through the API on purpose: seeding rows with EF would bypass
        // IActivityLogWriter and produce an empty feed that proves nothing.
        var create = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/tasks",
            new { columnId = todo.Id, title = "Task có hoạt động" });
        var task = await create.Content.ReadFromJsonAsync<CreatedTaskDto>();

        await client.PostJsonAsync($"/api/tasks/{task!.Id}/comments", new { content = "Bình luận" });
        await client.PutJsonAsync(
            $"/api/boards/{board.Id}/tasks/{task.Id}/move",
            new { columnId = todo.Id, position = 0 });

        var page = await client.GetJsonAsync<ActivityPageDto>(
            $"/api/workspaces/{workspace.Id}/activity");

        Assert.NotNull(page);
        Assert.True(page!.Items.Count >= 3, $"Expected at least 3 rows, got {page.Items.Count}.");

        // Newest first: created_at must be non-increasing.
        for (var i = 1; i < page.Items.Count; i++)
        {
            Assert.True(
                page.Items[i - 1].CreatedAt >= page.Items[i].CreatedAt,
                "Activity must be ordered newest first.");
        }

        Assert.Contains(page.Items, i => i.Action == "TaskCreated");
        Assert.Contains(page.Items, i => i.Action == "CommentAdded");
        Assert.Contains(page.Items, i => i.Action == "TaskMoved");
        Assert.False(page.HasMore);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Activity_FiltersByBoardAndExcludesWorkspaceLevelRows()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (owner, workspace, board, todo, _) = await SeedAsync(scenario);

        using var client = await scenario.AsUserAsync(owner);

        // One board-scoped row …
        await client.PostJsonAsync(
            $"/api/boards/{board.Id}/tasks",
            new { columnId = todo.Id, title = "Task trên board" });

        // … and one workspace-level row (board_id = NULL).
        await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}",
            new { name = "Đổi tên để sinh log", description = (string?)null });

        var boardPage = await client.GetJsonAsync<ActivityPageDto>(
            $"/api/workspaces/{workspace.Id}/activity?boardId={board.Id}");

        Assert.NotNull(boardPage);
        Assert.NotEmpty(boardPage!.Items);
        Assert.All(boardPage.Items, i => Assert.Equal(board.Id, i.BoardId));
        Assert.DoesNotContain(boardPage.Items, i => i.Action == ObserverActivityActions.WorkspaceUpdated);
    }

    [Theory]
    [InlineData("action=TaskMoved")]
    [InlineData("entityType=Comment")]
    public async Task Activity_FiltersByActionAndEntityType(string filter)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (owner, workspace, board, todo, _) = await SeedAsync(scenario);

        using var client = await scenario.AsUserAsync(owner);

        var create = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/tasks",
            new { columnId = todo.Id, title = "Task" });
        var task = await create.Content.ReadFromJsonAsync<CreatedTaskDto>();

        await client.PostJsonAsync($"/api/tasks/{task!.Id}/comments", new { content = "Bình luận" });
        await client.PutJsonAsync(
            $"/api/boards/{board.Id}/tasks/{task.Id}/move",
            new { columnId = todo.Id, position = 0 });

        var page = await client.GetJsonAsync<ActivityPageDto>(
            $"/api/workspaces/{workspace.Id}/activity?{filter}");

        Assert.NotNull(page);
        Assert.NotEmpty(page!.Items);

        if (filter.StartsWith("action=", StringComparison.Ordinal))
        {
            Assert.All(page.Items, i => Assert.Equal("TaskMoved", i.Action));
        }
        else
        {
            Assert.All(page.Items, i => Assert.Equal("Comment", i.EntityType));
        }
    }

    [Fact]
    public async Task Activity_ClampsTakeToTheMaximum()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (owner, workspace, _, _, _) = await SeedAsync(scenario);

        // 205 rows is enough to prove the 200 cap and keeps the test deterministic.
        await SeedActivityRowsAsync(scenario, workspace.Id, owner.Id, 205);

        using var client = await scenario.AsUserAsync(owner);
        var page = await client.GetJsonAsync<ActivityPageDto>(
            $"/api/workspaces/{workspace.Id}/activity?take=1000");

        Assert.NotNull(page);
        Assert.Equal(200, page!.Items.Count);
        Assert.True(page.HasMore);
        Assert.NotNull(page.NextCursor);
    }

    [Fact]
    public async Task Activity_PagingWithTheCursor_NeitherSkipsNorRepeatsRows()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (owner, workspace, _, _, _) = await SeedAsync(scenario);

        await SeedActivityRowsAsync(scenario, workspace.Id, owner.Id, 7);

        using var client = await scenario.AsUserAsync(owner);

        var seen = new List<Guid>();
        string? cursor = null;

        // 3 pages of 3 → 3 + 3 + 1.
        for (var page = 0; page < 3; page++)
        {
            var url = $"/api/workspaces/{workspace.Id}/activity?take=3"
                      + (cursor is null ? string.Empty : $"&before={Uri.EscapeDataString(cursor)}");

            var result = await client.GetJsonAsync<ActivityPageDto>(url);
            Assert.NotNull(result);

            seen.AddRange(result!.Items.Select(i => i.Id));
            cursor = result.NextCursor;

            if (!result.HasMore)
            {
                Assert.Null(cursor);
                break;
            }
        }

        Assert.Equal(7, seen.Count);
        Assert.Equal(7, seen.Distinct().Count());   // no duplicates across pages
    }

    [Theory]
    [InlineData("rac-khong-co-dau-pipe")]
    [InlineData("khong-phai-ngay|11111111-1111-1111-1111-111111111111")]
    [InlineData("2026-09-14T00:00:00Z|khong-phai-guid")]
    [InlineData("|11111111-1111-1111-1111-111111111111")]
    [InlineData("2026-09-14T00:00:00Z|")]
    public async Task Activity_WithAMalformedCursor_Returns400(string before)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (owner, workspace, _, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(owner);

        var response = await client.GetAsync(
            $"/api/workspaces/{workspace.Id}/activity?before={Uri.EscapeDataString(before)}");

        // 400, never 500 — and never a silent restart from page 1 (which would loop the UI).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Activity_DoesNotLeakAnotherWorkspacesHistory()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        var owner = await scenario.CreateUserAsync("Chủ A");
        var other = await scenario.CreateUserAsync("Chủ B");
        var workspace = await scenario.CreateWorkspaceAsync(owner, "Workspace A");
        var otherWorkspace = await scenario.CreateWorkspaceAsync(other, "Workspace B");

        await SeedActivityRowsAsync(scenario, workspace.Id, owner.Id, 2);
        await SeedActivityRowsAsync(scenario, otherWorkspace.Id, other.Id, 5);

        using var client = await scenario.AsUserAsync(owner);
        var page = await client.GetJsonAsync<ActivityPageDto>(
            $"/api/workspaces/{workspace.Id}/activity?take=100");

        Assert.NotNull(page);
        Assert.Equal(2, page!.Items.Count);
    }

    [Fact]
    public async Task Activity_ToleratesNullPayloadAndSystemActor()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (owner, workspace, _, _, _) = await SeedAsync(scenario);

        // A system-produced row (user_id NULL) with no payload — e.g. a future background job.
        await using (var db = scenario.NewDbContext())
        {
            db.Activities.Add(new ActivityLog
            {
                WorkspaceId = workspace.Id,
                BoardId = null,
                UserId = null,
                EntityType = ObserverEntityTypes.Workspace,
                EntityId = workspace.Id,
                Action = ObserverActivityActions.WorkspaceUpdated,
                Payload = null,
            });

            await db.SaveChangesAsync();
        }

        using var client = await scenario.AsUserAsync(owner);
        var page = await client.GetJsonAsync<ActivityPageDto>(
            $"/api/workspaces/{workspace.Id}/activity");

        Assert.NotNull(page);
        var item = Assert.Single(page!.Items);
        Assert.Null(item.UserId);
        Assert.Null(item.UserDisplayName);
        Assert.Null(item.Payload);
    }

    [Fact]
    public async Task Activity_ResolvesTheActorDisplayName()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (owner, workspace, _, _, _) = await SeedAsync(scenario);

        using var client = await scenario.AsUserAsync(owner);
        await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}",
            new { name = "Tên mới", description = (string?)null });

        var page = await client.GetJsonAsync<ActivityPageDto>(
            $"/api/workspaces/{workspace.Id}/activity?action=WorkspaceUpdated");

        Assert.NotNull(page);
        var item = Assert.Single(page!.Items);
        Assert.Equal(owner.Id, item.UserId);
        Assert.Equal("Trưởng nhóm", item.UserDisplayName);
        Assert.NotNull(item.Payload);
    }

    // ---- helpers ------------------------------------------------------------

    private static async Task<(ApplicationUser User, Workspace Workspace, Board Board, BoardColumn Todo, BoardColumn Done)>
        SeedAsync(TestScenario scenario)
    {
        var user = await scenario.CreateUserAsync("Trưởng nhóm");
        var workspace = await scenario.CreateWorkspaceAsync(user, "Workspace A");
        var (board, todo, done) = await scenario.CreateBoardAsync(workspace);

        return (user, workspace, board, todo, done);
    }

    /// <summary>
    /// Adds a workspace member flagged as the AI Agent — the shape
    /// <c>WorkspaceAiAgentResolver.EnsureAgentAsync</c> produces, without having to call it.
    /// </summary>
    private static async Task<ApplicationUser> CreateAiAgentAsync(
        TestScenario scenario, Workspace workspace, string agentName)
    {
        var agent = await scenario.CreateUserAsync(agentName);

        await using var db = scenario.NewDbContext();
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = agent.Id,
            Role = WorkspaceRole.Member,
            MemberType = MemberType.AiAgent,
            AiAgentName = agentName,
        });

        await db.SaveChangesAsync();
        return agent;
    }

    private static async Task<WorkspaceMember?> FindMembershipAsync(
        TestScenario scenario, Guid workspaceId, Guid userId)
    {
        await using var db = scenario.NewDbContext();
        return await db.WorkspaceMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(wm => wm.WorkspaceId == workspaceId && wm.UserId == userId);
    }

    /// <summary>Reads activity rows straight from the database (bypassing the endpoint's role gate).</summary>
    private static async Task<List<ActivityLog>> ReadActivityAsync(
        TestScenario scenario, Guid workspaceId, string action)
    {
        await using var db = scenario.NewDbContext();
        return await db.Activities
            .AsNoTracking()
            .Where(a => a.WorkspaceId == workspaceId && a.Action == action)
            .ToListAsync();
    }

    /// <summary>
    /// Inserts <paramref name="count"/> activity rows with distinct, increasing timestamps.
    /// Written with raw SQL so the timestamp is set explicitly: one INSERT per row would take longer
    /// and EF would overwrite <c>CreatedAt</c> through the audit convention.
    /// </summary>
    private static async Task SeedActivityRowsAsync(
        TestScenario scenario, Guid workspaceId, Guid userId, int count)
    {
        await using var db = scenario.NewDbContext();

        for (var i = 0; i < count; i++)
        {
            var createdAt = DateTimeOffset.UtcNow.AddSeconds(i);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO activity_logs
                    (id, workspace_id, board_id, user_id, entity_type, entity_id, action, payload, created_at, updated_at)
                VALUES
                    ({0}, {1}, NULL, {2}, 'Task', NULL, 'TaskUpdated', NULL, {3}, {3})
                """,
                Guid.NewGuid(),
                workspaceId,
                userId,
                createdAt);
        }
    }

    // ---- response shapes mirrored from the module DTOs ----------------------

    /// <summary>Just enough of the created task to drive the follow-up requests.</summary>
    private sealed record CreatedTaskDto
    {
        public Guid Id { get; init; }
        public string? Title { get; init; }
    }

    private sealed record WorkspaceSummaryDto
    {
        public Guid Id { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public string? Role { get; init; }
        public Guid OwnerId { get; init; }
        public bool IsOwner { get; init; }
    }

    private sealed record WorkspaceDetailDto
    {
        public Guid Id { get; init; }
        public string? Name { get; init; }
        public string? Description { get; init; }
        public Guid OwnerId { get; init; }
        public string? OwnerDisplayName { get; init; }
        public int MemberCount { get; init; }
        public int BoardCount { get; init; }
        public string? CurrentUserRole { get; init; }
    }

    private sealed record ActivityItemDto
    {
        public Guid Id { get; init; }
        public Guid? BoardId { get; init; }
        public Guid? UserId { get; init; }
        public string? UserDisplayName { get; init; }
        public string? EntityType { get; init; }
        public string? Action { get; init; }
        public JsonElement? Payload { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    private sealed record ActivityPageDto
    {
        public List<ActivityItemDto> Items { get; init; } = [];
        public string? NextCursor { get; init; }
        public bool HasMore { get; init; }
    }
}
