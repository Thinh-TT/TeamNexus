using System.Net;
using System.Net.Http.Json;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 8 §2.4 — the Kanban contract: workspace-scoped access control, board/column/task CRUD,
/// drag-and-drop moves, the <c>is_done</c> side effect, the clarification-column rules and the
/// assignee-membership guard introduced in Phase 7 §3.2.
/// </summary>
public sealed class KanbanApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public KanbanApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- access control -----------------------------------------------------

    [Fact]
    public async Task Boards_WithoutAToken_Returns401()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (_, workspace, _, _, _) = await SeedAsync(scenario);

        using var anonymous = await scenario.AnonymousAsync();
        var response = await anonymous.GetAsync($"/api/workspaces/{workspace.Id}/boards");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Boards_ForANonMember_Returns404AndLeaksNothing()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (_, workspace, _, _, _) = await SeedAsync(scenario);

        // An outsider: authenticated, but not a member of this workspace.
        var outsider = await scenario.CreateUserAsync("Người ngoài");
        using var client = await scenario.AsUserAsync(outsider);

        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}/boards");

        // 404 (not 403) on purpose: the caller must not be able to probe for workspace existence.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Boards_ForAMember_ListsTheWorkspaceBoards()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, workspace, board, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var boards = await client.GetJsonAsync<List<BoardResponse>>($"/api/workspaces/{workspace.Id}/boards");

        Assert.NotNull(boards);
        var listed = Assert.Single(boards!);
        Assert.Equal(board.Id, listed.Id);
        Assert.Equal("Board Test", listed.Name);
    }

    // ---- board CRUD ---------------------------------------------------------

    [Fact]
    public async Task Board_CreateUpdateDelete_RoundTrips()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, workspace, _, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var create = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/boards",
            new { name = "Bảng mới", description = "Mô tả" });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<BoardResponse>();
        Assert.NotNull(created);
        Assert.Equal("Bảng mới", created!.Name);

        var update = await client.PutJsonAsync(
            $"/api/workspaces/{workspace.Id}/boards/{created.Id}",
            new { name = "Bảng đã đổi", description = (string?)null });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var fetched = await client.GetJsonAsync<BoardResponse>(
            $"/api/workspaces/{workspace.Id}/boards/{created.Id}");
        Assert.Equal("Bảng đã đổi", fetched!.Name);

        var delete = await client.DeleteAsync($"/api/workspaces/{workspace.Id}/boards/{created.Id}");
        Assert.True(
            delete.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK,
            $"Expected 204/200, got {(int)delete.StatusCode}.");

        // Soft delete: the board disappears from the workspace-scoped read.
        var afterDelete = await client.GetAsync($"/api/workspaces/{workspace.Id}/boards/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Board_GetForAnUnknownId_Returns404()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, workspace, _, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}/boards/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- column CRUD & reorder ---------------------------------------------

    [Fact]
    public async Task Column_CreateAppendsAtTheEndOfTheBoard()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var create = await client.PostJsonAsync($"/api/boards/{board.Id}/columns", new { name = "Review" });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ColumnResponse>();

        Assert.NotNull(created);
        Assert.False(created!.IsDone);
        Assert.False(created.IsClarification);

        // The seeded board already has Todo(0) and Done(1), so the new column lands at 2.
        Assert.Equal(2, created.Position);
    }

    [Fact]
    public async Task Column_Reorder_PersistsTheNewOrder()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var columns = await client.GetJsonAsync<List<ColumnResponse>>($"/api/boards/{board.Id}/columns");
        Assert.NotNull(columns);
        Assert.Equal(2, columns!.Count);


        var todo = columns.Single(c => c.Name == "Todo");
        var done = columns.Single(c => c.Name == "Done");

        // Swap: Done(0), Todo(1).
        var reorder = await client.PutJsonAsync(
            $"/api/boards/{board.Id}/columns/reorder",
            new { items = new[] { new { id = done.Id, position = 0 }, new { id = todo.Id, position = 1 } } });

        Assert.Equal(HttpStatusCode.NoContent, reorder.StatusCode);

        var after = await client.GetJsonAsync<List<ColumnResponse>>($"/api/boards/{board.Id}/columns");
        Assert.NotNull(after);

        Assert.Equal("Done", after!.Single(c => c.Position == 0).Name);
        Assert.Equal("Todo", after.Single(c => c.Position == 1).Name);
    }

    [Fact]
    public async Task ClarificationColumn_CannotAlsoBeADoneColumn()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        // is_done AND is_clarification are mutually exclusive (Phase 7 §3.5) → 400.
        var response = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/columns",
            new { name = "Chờ làm rõ", isDone = true, isClarification = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await scenario.CountAsync<BoardColumn>(c => c.Name == "Chờ làm rõ"));
    }

    [Fact]
    public async Task ReopeningADoneColumn_IsAllowed()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, _, done) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var response = await client.PutJsonAsync(
            $"/api/boards/{board.Id}/columns/{done.Id}",
            new { name = "Done", isDone = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await client.GetJsonAsync<List<ColumnResponse>>($"/api/boards/{board.Id}/columns");

        Assert.False(after!.Single(c => c.Id == done.Id).IsDone);
    }

    // ---- task CRUD ----------------------------------------------------------

    [Fact]
    public async Task Task_Create_ReturnsTheCreatedTaskWithItsColumn()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var create = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/tasks",
            new { columnId = todo.Id, title = "Viết tài liệu", description = "Chi tiết", priority = "High" });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<TaskResponse>();

        Assert.NotNull(created);
        Assert.Equal("Viết tài liệu", created!.Title);
        Assert.Equal(todo.Id, created.ColumnId);
        Assert.Equal("High", created.Priority);
        Assert.False(created.AssigneeIsAiAgent);
    }

    [Fact]
    public async Task Task_Update_ChangesTheTitle()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, _) = await SeedAsync(scenario);
        var task = await scenario.CreateTaskAsync(board, todo, user, "Tiêu đề cũ");
        using var client = await scenario.AsUserAsync(user);

        var response = await client.PutJsonAsync(
            $"/api/boards/{board.Id}/tasks/{task.Id}",
            new { title = "Tiêu đề mới", description = (string?)null, priority = "Urgent" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.Equal("Tiêu đề mới", updated!.Title);
        Assert.Equal("Urgent", updated.Priority);
    }

    [Fact]
    public async Task Task_Delete_IsASoftDeleteAndTheTaskDisappearsFromReads()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, _) = await SeedAsync(scenario);
        var task = await scenario.CreateTaskAsync(board, todo, user, "Sẽ bị xoá");
        using var client = await scenario.AsUserAsync(user);

        var delete = await client.DeleteAsync($"/api/boards/{board.Id}/tasks/{task.Id}");
        Assert.True(
            delete.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK,
            $"Expected 204/200, got {(int)delete.StatusCode}.");

        var get = await client.GetAsync($"/api/boards/{board.Id}/tasks/{task.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        // Soft delete: the row survives with a DeletedAt marker (audit + reporting history). The
        // global query filter hides it from normal reads, so the check must ignore that filter.
        var stillInDatabase = await scenario.FindIncludingSoftDeletedAsync<BoardTask>(task.Id);
        Assert.NotNull(stillInDatabase);
        Assert.NotNull(stillInDatabase!.DeletedAt);
    }

    // ---- drag & drop + the is_done side effect ------------------------------

    [Fact]
    public async Task Move_IntoADoneColumn_SetsCompletedAt()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, done) = await SeedAsync(scenario);
        var task = await scenario.CreateTaskAsync(board, todo, user, "Hoàn thành");
        using var client = await scenario.AsUserAsync(user);

        var move = await client.PutJsonAsync(
            $"/api/boards/{board.Id}/tasks/{task.Id}/move",
            new { columnId = done.Id, position = 0 });

        Assert.Equal(HttpStatusCode.OK, move.StatusCode);

        var moved = await move.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.Equal(done.Id, moved!.ColumnId);
        Assert.NotNull(moved.CompletedAt);
    }

    [Fact]
    public async Task Move_OutOfADoneColumn_ClearsCompletedAt()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, done) = await SeedAsync(scenario);
        var task = await scenario.CreateTaskAsync(board, todo, user, "Mở lại");
        using var client = await scenario.AsUserAsync(user);

        // Done first …
        await client.PutJsonAsync(
            $"/api/boards/{board.Id}/tasks/{task.Id}/move",
            new { columnId = done.Id, position = 0 });

        // … then back to Todo: CompletedAt must be cleared, otherwise reporting would count it done.
        var back = await client.PutJsonAsync(
            $"/api/boards/{board.Id}/tasks/{task.Id}/move",
            new { columnId = todo.Id, position = 0 });

        var moved = await back.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.Equal(todo.Id, moved!.ColumnId);
        Assert.Null(moved.CompletedAt);
    }

    [Fact]
    public async Task Move_WithinTheSameColumn_RenumbersPositionsDensely()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, _) = await SeedAsync(scenario);

        var first = await scenario.CreateTaskAsync(board, todo, user, "Thứ nhất");
        var second = await scenario.CreateTaskAsync(board, todo, user, "Thứ hai");
        var third = await scenario.CreateTaskAsync(board, todo, user, "Thứ ba");

        using var client = await scenario.AsUserAsync(user);

        // Requesting position 5 clamps to the end, and the whole column is renumbered 0..n-1 —
        // the contract is "dense positions after a drag & drop", not "honour the raw index".
        var response = await client.PutJsonAsync(
            $"/api/boards/{board.Id}/tasks/{first.Id}/move",
            new { columnId = todo.Id, position = 5 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var moved = await response.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.Equal(2, moved!.Position);
        Assert.Null(moved.CompletedAt);

        var all = await client.GetJsonAsync<List<TaskResponse>>($"/api/boards/{board.Id}/tasks");
        Assert.NotNull(all);

        var positions = all!.Where(t => t.ColumnId == todo.Id).OrderBy(t => t.Position)
            .Select(t => (t.Title, t.Position)).ToList();

        Assert.Equal(
            [("Thứ hai", 0), ("Thứ ba", 1), ("Thứ nhất", 2)],
            positions);
        Assert.Contains(all, t => t.Id == second.Id);
        Assert.Contains(all, t => t.Id == third.Id);
    }

    // ---- the Phase 7 §3.2 membership guard (regression net) -----------------

    [Fact]
    public async Task CreateTask_WithAnAssigneeOutsideTheWorkspace_IsRejectedWith400()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, _) = await SeedAsync(scenario);

        // A real user, but NOT a member of this workspace. Before Phase 7 §3.2 this was accepted,
        // which is exactly the hole the AI Agent would have walked through.
        var outsider = await scenario.CreateUserAsync("Người ngoài workspace");
        using var client = await scenario.AsUserAsync(user);
        var before = await scenario.CountAsync<BoardTask>();

        var response = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/tasks",
            new { columnId = todo.Id, title = "Giao cho người ngoài", assigneeId = outsider.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await scenario.CountAsync<BoardTask>());
    }

    [Fact]
    public async Task UpdateTask_WithAnAssigneeOutsideTheWorkspace_IsRejectedWith400()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, _) = await SeedAsync(scenario);
        var task = await scenario.CreateTaskAsync(board, todo, user, "Đổi người ngoài");
        var outsider = await scenario.CreateUserAsync("Người ngoài khác");
        using var client = await scenario.AsUserAsync(user);

        var response = await client.PutJsonAsync(
            $"/api/boards/{board.Id}/tasks/{task.Id}",
            new { title = "Đổi người ngoài", assigneeId = outsider.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var unchanged = await scenario.FindAsync<BoardTask>(task.Id);
        Assert.Null(unchanged!.AssigneeId);
    }

    [Fact]
    public async Task UpdateTask_WithAMemberAssignee_IsAcceptedAndResolvesTheDisplayName()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        var owner = await scenario.CreateUserAsync("Chủ workspace");
        var member = await scenario.CreateUserAsync("Thành viên");
        var workspace = await scenario.CreateWorkspaceAsync(owner, "WS", (member, WorkspaceRole.Member));
        var (board, todo, _) = await scenario.CreateBoardAsync(workspace);
        var task = await scenario.CreateTaskAsync(board, todo, owner, "Giao đúng người");
        using var client = await scenario.AsUserAsync(owner);

        var response = await client.PutJsonAsync(
            $"/api/boards/{board.Id}/tasks/{task.Id}",
            new { title = "Giao đúng người", assigneeId = member.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.Equal(member.Id, updated!.AssigneeId);
        Assert.Equal("Thành viên", updated.AssigneeName);
        Assert.False(updated.AssigneeIsAiAgent);
    }

    // ---- comments & labels --------------------------------------------------

    [Fact]
    public async Task Comment_CreateAndSoftDelete()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, _) = await SeedAsync(scenario);
        var task = await scenario.CreateTaskAsync(board, todo, user, "Task có comment");
        using var client = await scenario.AsUserAsync(user);

        var create = await client.PostJsonAsync(
            $"/api/tasks/{task.Id}/comments",
            new { content = "Bình luận đầu tiên" });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var comment = await create.Content.ReadFromJsonAsync<CommentResponse>();
        Assert.NotNull(comment);
        Assert.Equal("Bình luận đầu tiên", comment!.Content);

        var delete = await client.DeleteAsync($"/api/tasks/{task.Id}/comments/{comment.Id}");
        Assert.True(
            delete.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK,
            $"Expected 204/200, got {(int)delete.StatusCode}.");

        var remaining = await client.GetJsonAsync<List<CommentResponse>>($"/api/tasks/{task.Id}/comments");

        Assert.Empty(remaining!);
    }

    [Fact]
    public async Task Labels_AreScopedToTheWorkspaceAndCanBeAttached()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, workspace, board, todo, _) = await SeedAsync(scenario);
        var task = await scenario.CreateTaskAsync(board, todo, user, "Task có nhãn");
        using var client = await scenario.AsUserAsync(user);

        var createLabel = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/labels",
            new { name = "Ưu tiên cao", color = "#FF0000" });

        Assert.True(
            createLabel.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            $"Expected 200/201, got {(int)createLabel.StatusCode}.");

        var label = await createLabel.Content.ReadFromJsonAsync<LabelResponse>();
        Assert.NotNull(label);

        var attach = await client.PostJsonAsync($"/api/tasks/{task.Id}/labels", new { labelId = label!.Id });

        Assert.True(
            attach.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK,
            $"Expected 200/204, got {(int)attach.StatusCode}.");

        var fetched = await client.GetJsonAsync<TaskResponse>($"/api/boards/{board.Id}/tasks/{task.Id}");

        Assert.Contains(fetched!.Labels, l => l.Id == label.Id);
    }

    [Fact]
    public async Task Members_RequireMembershipAndExposeTheWorkspaceRoles()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, workspace, _, _, _) = await SeedAsync(scenario);

        // A workspace lazily creates its single AI Agent member (Phase 7 D1), so exactly two entries
        // are expected: the human Admin and the agent.
        using var client = await scenario.AsUserAsync(user);
        var members = await client.GetJsonAsync<List<MemberResponse>>($"/api/workspaces/{workspace.Id}/members");

        Assert.Equal(2, members!.Count);
        Assert.Contains(members, m => m.UserId == user.Id && m.Role == "Admin");
        Assert.Contains(members, m => m.DisplayName == "TeamNexus Agent" && m.MemberType == "ai_agent");

        // An outsider cannot even enumerate the members.
        var outsider = await scenario.CreateUserAsync("Người ngoài members");
        using var outsiderClient = await scenario.AsUserAsync(outsider);
        var forbidden = await outsiderClient.GetAsync($"/api/workspaces/{workspace.Id}/members");

        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
    }

    // ---- helpers ------------------------------------------------------------

    private static async Task<(ApplicationUser User, Workspace Workspace, Board Board, BoardColumn Todo, BoardColumn Done)>
        SeedAsync(TestScenario scenario)
    {
        var user = await scenario.CreateUserAsync("Trưởng nhóm");
        var workspace = await scenario.CreateWorkspaceAsync(user, "Workspace Kanban");
        var (board, todo, done) = await scenario.CreateBoardAsync(workspace);

        return (user, workspace, board, todo, done);
    }

    // ---- response shapes mirrored from the module DTOs ----------------------

    private sealed record BoardResponse(Guid Id, Guid WorkspaceId, string Name, string? Description);

    private sealed record CommentResponse(Guid Id, Guid TaskId, string Content, string? AuthorName);

    private sealed record LabelResponse(Guid Id, Guid WorkspaceId, string Name, string Color);

    private sealed record MemberResponse(
        Guid UserId, string DisplayName, string Role, string? AvatarUrl, string MemberType);
}
