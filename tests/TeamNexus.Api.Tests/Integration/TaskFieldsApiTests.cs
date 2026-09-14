using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 10 §1 — the task metadata contract for the three fields the roadmap exposes in the UI:
/// <c>description</c>, <c>due_date</c> and <c>priority</c>.
/// <para>
/// <b>Why this suite exists:</b> the backend already implemented all three fields in Phase 2, but no
/// test pinned them. A change to <c>TaskService</c> or <c>DtoMapping.MapTask</c> — for example
/// forgetting to resolve a field on ONE of the three task-returning paths — would silently break the
/// Due Date / Priority / Description boxes of Giai đoạn 10 while the suite stayed green.
/// </para>
/// <para>
/// <c>TaskResponse</c> carries 17 properties whose parameterless constructor makes the positional
/// parameters optional, so a field the API stops sending deserializes to <c>null</c> instead of
/// throwing — which is exactly the regression shape these tests must be able to see.
/// </para>
/// </summary>
public sealed class TaskFieldsApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public TaskFieldsApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- create: all three fields round-trip ---------------------------------

    [Fact]
    public async Task CreateTask_PersistsDescriptionDueDateAndPriority()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var dueDate = new DateTimeOffset(2026, 12, 31, 17, 30, 0, TimeSpan.Zero);

        var create = await client.PostJsonAsync($"/api/boards/{board.Id}/tasks", new
        {
            columnId = todo.Id,
            title = "Viết tài liệu Phase 10",
            description = "Mô tả chi tiết",
            dueDate,
            priority = "High",
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<TaskDto>();
        Assert.NotNull(created);
        Assert.Equal("Mô tả chi tiết", created!.Description);
        Assert.Equal(dueDate, created.DueDate);
        Assert.Equal("High", created.Priority);
    }

    // ---- every task-returning path resolves the three fields ------------------

    [Fact]
    public async Task TaskFields_AreResolvedOnTheTaskListPath()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var dueDate = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
        await CreateTaskAsync(client, board.Id, todo.Id, "Task trong danh sách", dueDate, "Medium");

        var tasks = await client.GetJsonAsync<List<TaskDto>>($"/api/boards/{board.Id}/tasks");
        Assert.NotNull(tasks);

        var listed = tasks!.Single(t => t.Title == "Task trong danh sách");
        Assert.Equal("Mô tả markdown", listed.Description);
        Assert.Equal(dueDate, listed.DueDate);
        Assert.Equal("Medium", listed.Priority);
    }

    [Fact]
    public async Task TaskFields_AreResolvedOnTheSingleTaskPath()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var dueDate = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var created = await CreateTaskAsync(client, board.Id, todo.Id, "Task chi tiết", dueDate, "Urgent");

        var fetched = await client.GetJsonAsync<TaskDto>($"/api/boards/{board.Id}/tasks/{created.Id}");

        Assert.NotNull(fetched);
        Assert.Equal("Mô tả markdown", fetched!.Description);
        Assert.Equal(dueDate, fetched.DueDate);
        Assert.Equal("Urgent", fetched.Priority);
    }

    /// <summary>
    /// The third path: the full-board read, whose tasks arrive nested inside their column. Phase 7 §3.3
    /// recorded this exact shape of bug (a new <c>TaskResponse</c> field resolved on two paths but not
    /// the third), so every new field must be asserted here too.
    /// </summary>
    [Fact]
    public async Task TaskFields_AreResolvedOnTheNestedBoardPath()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, workspace, board, todo, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(user);

        var dueDate = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        await CreateTaskAsync(client, board.Id, todo.Id, "Task trong board", dueDate, "Low");

        var full = await client.GetJsonAsync<BoardDto>($"/api/workspaces/{workspace.Id}/boards/{board.Id}");
        Assert.NotNull(full);

        var nested = full!.Columns
            .SelectMany(c => c.Tasks)
            .Single(t => t.Title == "Task trong board");

        Assert.Equal("Mô tả markdown", nested.Description);
        Assert.Equal(dueDate, nested.DueDate);
        Assert.Equal("Low", nested.Priority);
    }

    // ---- update: all three fields -------------------------------------------

    [Fact]
    public async Task UpdateTask_ChangesPriority()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, _) = await SeedAsync(scenario);
        var task = await scenario.CreateTaskAsync(board, todo, user, "Đổi ưu tiên");
        using var client = await scenario.AsUserAsync(user);

        var response = await client.PutJsonAsync(
            $"/api/boards/{board.Id}/tasks/{task.Id}",
            new { title = "Đổi ưu tiên", priority = "Urgent" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<TaskDto>();
        Assert.Equal("Urgent", updated!.Priority);
    }

    [Fact]
    public async Task UpdateTask_AcceptsPriorityInAnyCaseAndStoresTheCanonicalName()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, _) = await SeedAsync(scenario);
        var task = await scenario.CreateTaskAsync(board, todo, user, "Ưu tiên chữ thường");
        using var client = await scenario.AsUserAsync(user);

        // ParsePriority uses ignoreCase: true, so the wire value may be lowercase; the CHECK
        // constraint and every read then see the canonical enum name.
        var response = await client.PutJsonAsync(
            $"/api/boards/{board.Id}/tasks/{task.Id}",
            new { title = "Ưu tiên chữ thường", priority = "urgent" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<TaskDto>();
        Assert.Equal("Urgent", updated!.Priority);

        var persisted = await scenario.FindAsync<BoardTask>(task.Id);
        Assert.Equal(TaskPriority.Urgent, persisted!.Priority);
    }

    [Theory]
    [InlineData("Critical")]
    [InlineData("1")]
    [InlineData("+1")]
    [InlineData("99")]
    [InlineData("HIGHEST")]
    [InlineData("")]
    public async Task UpdateTask_WithAnUnknownPriority_Returns400(string priority)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, _) = await SeedAsync(scenario);
        var task = await scenario.CreateTaskAsync(board, todo, user, "Ưu tiên sai");
        using var client = await scenario.AsUserAsync(user);

        var response = await client.PutJsonAsync(
            $"/api/boards/{board.Id}/tasks/{task.Id}",
            new { title = "Ưu tiên sai", priority });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(body);
        Assert.Contains("Low", body!.Error);
        Assert.Contains("Urgent", body.Error);

        // Nothing was written: the task keeps the priority it had (null).
        var persisted = await scenario.FindAsync<BoardTask>(task.Id);
        Assert.Null(persisted!.Priority);
    }

    [Fact]
    public async Task UpdateTask_WithANullDueDate_ClearsTheDeadline()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, _) = await SeedAsync(scenario);
        var task = await scenario.CreateTaskAsync(
            board, todo, user, "Xoá hạn", dueDate: DateTimeOffset.UtcNow.AddDays(3));
        using var client = await scenario.AsUserAsync(user);

        var response = await client.PutJsonAsync(
            $"/api/boards/{board.Id}/tasks/{task.Id}",
            new { title = "Xoá hạn", dueDate = (DateTimeOffset?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<TaskDto>();
        Assert.Null(updated!.DueDate);

        var persisted = await scenario.FindAsync<BoardTask>(task.Id);
        Assert.Null(persisted!.DueDate);
    }

    /// <summary>
    /// The deadline is an instant, not a wall-clock string: "2026-06-15T09:00:00+07:00" and
    /// "2026-06-15T02:00:00Z" are the same moment. A UI that compared the raw string would mark the
    /// task overdue by up to 14 hours.
    /// </summary>
    [Fact]
    public async Task UpdateTask_WithAnOffsetDueDate_RoundTripsTheSameInstant()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, _) = await SeedAsync(scenario);
        var task = await scenario.CreateTaskAsync(board, todo, user, "Lệch múi giờ");
        using var client = await scenario.AsUserAsync(user);

        var withOffset = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.FromHours(7));

        var response = await client.PutJsonAsync(
            $"/api/boards/{board.Id}/tasks/{task.Id}",
            new { title = "Lệch múi giờ", dueDate = withOffset });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<TaskDto>();
        Assert.NotNull(updated!.DueDate);
        Assert.Equal(withOffset.UtcDateTime, updated.DueDate!.Value.UtcDateTime);
    }

    [Fact]
    public async Task UpdateTask_WithANullPriority_ClearsThePriority()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, _) = await SeedAsync(scenario);
        var task = await scenario.CreateTaskAsync(board, todo, user, "Xoá ưu tiên");
        using var client = await scenario.AsUserAsync(user);

        var response = await client.PutJsonAsync(
            $"/api/boards/{board.Id}/tasks/{task.Id}",
            new { title = "Xoá ưu tiên", priority = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<TaskDto>();
        Assert.Null(updated!.Priority);
    }

    [Fact]
    public async Task UpdateTask_WithABlankDescription_StoresNull()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, _) = await SeedAsync(scenario);
        var task = await scenario.CreateTaskAsync(board, todo, user, "Mô tả trắng");
        using var client = await scenario.AsUserAsync(user);

        var response = await client.PutJsonAsync(
            $"/api/boards/{board.Id}/tasks/{task.Id}",
            new { title = "Mô tả trắng", description = "   " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<TaskDto>();
        Assert.Null(updated!.Description);

        var persisted = await scenario.FindAsync<BoardTask>(task.Id);
        Assert.Null(persisted!.Description);
    }

    /// <summary>
    /// Markdown is the frontend's job (Phase 10 §3): the API must store the raw source verbatim and
    /// never escape, strip or reformat it — otherwise the preview would render different text than
    /// the editor shows.
    /// </summary>
    [Fact]
    public async Task UpdateTask_StoresMarkdownDescriptionVerbatim()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, _, board, todo, _) = await SeedAsync(scenario);
        var task = await scenario.CreateTaskAsync(board, todo, user, "Mô tả markdown");
        using var client = await scenario.AsUserAsync(user);

        const string markdown = "**Đậm** và `code` và [liên kết](https://example.com)\nDòng hai";

        var response = await client.PutJsonAsync(
            $"/api/boards/{board.Id}/tasks/{task.Id}",
            new { title = "Mô tả markdown", description = markdown });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<TaskDto>();
        Assert.Equal(markdown, updated!.Description);

        var persisted = await scenario.FindAsync<BoardTask>(task.Id);
        Assert.Equal(markdown, persisted!.Description);
    }

    // ---- the activity log payload (Phase 5 §2.3 contract) --------------------

    /// <summary>
    /// The Observer's activity row records the priority and the <c>dueDate</c> but reports the
    /// description as a boolean — a text field must never be copied into the log (token/cost control,
    /// Phase 5 §2.3 decision A6). This guards both halves of that rule.
    /// </summary>
    [Fact]
    public async Task UpdateTask_LogsPriorityAndDueDateButNeverTheDescriptionText()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (user, workspace, board, todo, _) = await SeedAsync(scenario);
        var task = await scenario.CreateTaskAsync(board, todo, user, "Ghi log thay đổi");
        using var client = await scenario.AsUserAsync(user);

        var dueDate = new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.Zero);
        const string secret = "Nội dung mô tả không được vào log";

        var response = await client.PutJsonAsync(
            $"/api/boards/{board.Id}/tasks/{task.Id}",
            new { title = "Ghi log thay đổi", description = secret, dueDate, priority = "High" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = scenario.NewDbContext();
        var logs = await db.Activities
            .AsNoTracking()
            .Where(a => a.WorkspaceId == workspace.Id
                        && a.Action == ObserverActivityActions.TaskUpdated
                        && a.EntityId == task.Id)
            .ToListAsync();

        var log = Assert.Single(logs);
        Assert.Equal(ObserverEntityTypes.Task, log.EntityType);
        Assert.Equal(board.Id, log.BoardId);
        Assert.Equal(user.Id, log.UserId);

        Assert.NotNull(log.Payload);
        var payload = JsonDocument.Parse(log.Payload!);

        Assert.Equal("High", payload.RootElement.GetProperty("priority").GetString());
        Assert.Equal(dueDate, payload.RootElement.GetProperty("dueDate").GetDateTimeOffset());
        Assert.True(payload.RootElement.GetProperty("descriptionChanged").GetBoolean());

        // The actual text must not appear anywhere in the row.
        Assert.DoesNotContain(secret, log.Payload, StringComparison.Ordinal);
    }

    // ---- helpers ------------------------------------------------------------

    private static async Task<TaskDto> CreateTaskAsync(
        TestHttpClient client, Guid boardId, Guid columnId, string title, DateTimeOffset dueDate, string priority)
    {
        var response = await client.PostJsonAsync($"/api/boards/{boardId}/tasks", new
        {
            columnId,
            title,
            description = "Mô tả markdown",
            dueDate,
            priority,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<TaskDto>();
        Assert.NotNull(created);
        return created!;
    }

    private static async Task<(ApplicationUser User, Workspace Workspace, Board Board, BoardColumn Todo, BoardColumn Done)>
        SeedAsync(TestScenario scenario)
    {
        var user = await scenario.CreateUserAsync("Trưởng nhóm");
        var workspace = await scenario.CreateWorkspaceAsync(user, "Workspace Task Fields");
        var (board, todo, done) = await scenario.CreateBoardAsync(workspace);

        return (user, workspace, board, todo, done);
    }

    // ---- response shapes mirrored from the module DTOs ----------------------
    //
    // Every property is nullable / defaulted so a field the API stops sending shows up as null
    // instead of a deserialization failure — that is what makes these tests able to fail.

    private sealed record ErrorResponse(string Error);

    private sealed record TaskDto
    {
        public Guid Id { get; init; }
        public string? Title { get; init; }
        public string? Description { get; init; }
        public DateTimeOffset? DueDate { get; init; }
        public string? Priority { get; init; }
    }

    private sealed record ColumnDto
    {
        public Guid Id { get; init; }
        public string? Name { get; init; }
        public List<TaskDto> Tasks { get; init; } = [];
    }

    private sealed record BoardDto
    {
        public Guid Id { get; init; }
        public Guid WorkspaceId { get; init; }
        public string? Name { get; init; }
        public List<ColumnDto> Columns { get; init; } = [];
    }
}
