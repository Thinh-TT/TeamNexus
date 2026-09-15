using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 12 §2 — cross-board task search (S-1…S-16): the text query, every filter, the keyset cursor
/// and the isolation rules (`GET /api/workspaces/{id}/tasks/search`).
/// <para>
/// Filter assertions go through HTTP, and the rows they filter on are seeded directly, because a
/// search test needs exact <c>created_at</c>/<c>updated_at</c> values that no endpoint can set. The
/// counterpart — that <c>GET /api/boards/{boardId}/tasks</c> still answers a bare array — is covered
/// by <c>KanbanApiTests</c>, which this suite must never break.
/// </para>
/// </summary>
public sealed class TaskSearchApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public TaskSearchApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- text query --------------------------------------------------------

    [Fact]
    public async Task Query_MatchesTitle_CaseInsensitively_AndWithVietnameseDiacritics()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, board, todo, _) = await SeedAsync(scenario);

        await SeedTaskAsync(scenario, board, todo, "Báo cáo tuần 12", me.Id, updatedAt: new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero));
        await SeedTaskAsync(scenario, board, todo, "Sửa lỗi đăng nhập", me.Id, updatedAt: new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));

        using var client = await scenario.AsUserAsync(me);

        var lower = await SearchRequiredAsync(client, workspace.Id, "báo cáo");
        Assert.Single(lower.Items);
        Assert.Equal("Báo cáo tuần 12", lower.Items[0].Task.Title);
        Assert.True(lower.HasQuery);

        var upper = await SearchRequiredAsync(client, workspace.Id, "BÁO CÁO");
        Assert.Single(upper.Items);
    }

    [Fact]
    public async Task Query_MatchesDescriptionToo_ButRanksTitleMatchesFirst()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, board, todo, _) = await SeedAsync(scenario);

        // The description-only hit is the MOST recently updated, so it would win on recency alone.
        // Only relevance ordering can put the title hit first.
        await SeedTaskAsync(
            scenario, board, todo, "Chỉ có trong mô tả", me.Id,
            description: "Nội dung nói về deploy production",
            updatedAt: new DateTimeOffset(2026, 5, 9, 0, 0, 0, TimeSpan.Zero));
        await SeedTaskAsync(
            scenario, board, todo, "Deploy production ngay", me.Id,
            updatedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        using var client = await scenario.AsUserAsync(me);
        var result = await SearchRequiredAsync(client, workspace.Id, "deploy");

        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Deploy production ngay", result.Items[0].Task.Title);
        Assert.Equal("Chỉ có trong mô tả", result.Items[1].Task.Title);
    }

    [Fact]
    public async Task BlankQuery_IsNoFilter_AndReportsHasQueryFalse()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, board, todo, _) = await SeedAsync(scenario);

        await SeedTaskAsync(scenario, board, todo, "Thẻ A", me.Id, updatedAt: new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));
        await SeedTaskAsync(scenario, board, todo, "Thẻ B", me.Id, updatedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        using var client = await scenario.AsUserAsync(me);

        var absent = await SearchRequiredAsync(client, workspace.Id, query: null);
        Assert.Equal(2, absent.Items.Count);
        Assert.False(absent.HasQuery);

        var spaces = await SearchRequiredAsync(client, workspace.Id, query: "   ");
        Assert.Equal(2, spaces.Items.Count);
        Assert.False(spaces.HasQuery);
    }

    [Fact]
    public async Task QueryLongerThanTwoHundredCharacters_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, _, _, _) = await SeedAsync(scenario);

        using var client = await scenario.AsUserAsync(me);

        var atLimit = await client.GetAsync(
            $"/api/workspaces/{workspace.Id}/tasks/search?q={new string('a', 200)}");
        Assert.Equal(HttpStatusCode.OK, atLimit.StatusCode);

        var overLimit = await client.GetAsync(
            $"/api/workspaces/{workspace.Id}/tasks/search?q={new string('a', 201)}");
        Assert.Equal(HttpStatusCode.BadRequest, overLimit.StatusCode);
    }

    // ---- filters -----------------------------------------------------------

    [Fact]
    public async Task BoardFilter_FromAnotherWorkspace_IsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, _, _, _) = await SeedAsync(scenario);

        var otherWorkspace = await scenario.CreateWorkspaceAsync(me, "Workspace khác");
        var (otherBoard, _, _) = await scenario.CreateBoardAsync(otherWorkspace, "Board ngoài");

        using var client = await scenario.AsUserAsync(me);
        var response = await client.GetAsync(
            $"/api/workspaces/{workspace.Id}/tasks/search?boardId={otherBoard.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BoardFilter_NarrowsToThatBoard()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, board, todo, _) = await SeedAsync(scenario);
        var (secondBoard, secondTodo, _) = await AddBoardAsync(scenario, workspace.Id, "Board hai", me);

        await SeedTaskAsync(scenario, board, todo, "Ở board một", me.Id, updatedAt: new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));
        await SeedTaskAsync(scenario, secondBoard, secondTodo, "Ở board hai", me.Id, updatedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        using var client = await scenario.AsUserAsync(me);

        var all = await SearchRequiredAsync(client, workspace.Id, query: null);
        Assert.Equal(2, all.Items.Count);

        var scoped = await SearchRequiredAsync(client, workspace.Id, query: null, boardId: secondBoard.Id);
        Assert.Single(scoped.Items);
        Assert.Equal("Ở board hai", scoped.Items[0].Task.Title);
        Assert.Equal("Board hai", scoped.Items[0].BoardName);
    }

    [Fact]
    public async Task AssigneeFilter_Works_AndAnOutsiderIsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, board, todo, _) = await SeedAsync(scenario);
        var colleague = await scenario.CreateUserAsync("Đồng nghiệp", "search.colleague@example.test");
        await AddMemberAsync(scenario, workspace.Id, colleague, WorkspaceRole.Member);
        var outsider = await scenario.CreateUserAsync("Người ngoài", "search.outsider@example.test");

        // Make the "outsider" an established user in a DIFFERENT workspace, so the 400 is about
        // membership of the searched workspace and not about a brand-new account with no rows at all.
        var otherWorkspace = await scenario.CreateWorkspaceAsync(outsider, "Workspace của người ngoài");

        await SeedTaskAsync(scenario, board, todo, "Việc của tôi", me.Id, assigneeId: me.Id, updatedAt: new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero));
        await SeedTaskAsync(scenario, board, todo, "Việc của đồng nghiệp", me.Id, assigneeId: colleague.Id, updatedAt: new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));
        await SeedTaskAsync(scenario, board, todo, "Chưa gán ai", me.Id, assigneeId: null, updatedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        using var client = await scenario.AsUserAsync(me);

        var mine = await SearchRequiredAsync(client, workspace.Id, query: null, assigneeId: me.Id);
        Assert.Single(mine.Items);
        Assert.Equal("Việc của tôi", mine.Items[0].Task.Title);

        var theirs = await SearchRequiredAsync(client, workspace.Id, query: null, assigneeId: colleague.Id);
        Assert.Single(theirs.Items);

        // Someone who is not in the workspace is a bad request: silently returning nothing would hide
        // a client bug (the id came from somewhere the workspace does not know about).
        var response = await client.GetAsync(
            $"/api/workspaces/{workspace.Id}/tasks/search?assigneeId={outsider.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnassignedFilter_ReturnsOnlyTasksWithoutAnAssignee()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, board, todo, _) = await SeedAsync(scenario);

        await SeedTaskAsync(scenario, board, todo, "Có người", me.Id, assigneeId: me.Id, updatedAt: new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));
        await SeedTaskAsync(scenario, board, todo, "Không người", me.Id, assigneeId: null, updatedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        using var client = await scenario.AsUserAsync(me);
        var result = await SearchRequiredAsync(client, workspace.Id, query: null, unassigned: true);

        Assert.Single(result.Items);
        Assert.Equal("Không người", result.Items[0].Task.Title);
        Assert.Null(result.Items[0].Task.AssigneeId);
    }

    [Fact]
    public async Task LabelFilter_IsAnAnd_ForeignLabelsAreRejected_AndMoreThanTenIsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, board, todo, _) = await SeedAsync(scenario);

        var (labelA, labelB) = await SeedLabelsAsync(scenario, workspace.Id, me, "Nhãn A", "Nhãn B");

        var both = await SeedTaskAsync(scenario, board, todo, "Có cả hai nhãn", me.Id, updatedAt: new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero));
        var onlyA = await SeedTaskAsync(scenario, board, todo, "Chỉ nhãn A", me.Id, updatedAt: new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));

        await AttachLabelAsync(scenario, both.Id, labelA.Id);
        await AttachLabelAsync(scenario, both.Id, labelB.Id);
        await AttachLabelAsync(scenario, onlyA.Id, labelA.Id);

        using var client = await scenario.AsUserAsync(me);

        var withA = await SearchRequiredAsync(client, workspace.Id, query: null, labelIds: [labelA.Id]);
        Assert.Equal(2, withA.Items.Count);

        // AND, not OR.
        var withBoth = await SearchRequiredAsync(client, workspace.Id, query: null, labelIds: [labelA.Id, labelB.Id]);
        Assert.Single(withBoth.Items);
        Assert.Equal("Có cả hai nhãn", withBoth.Items[0].Task.Title);

        // A label that belongs to ANOTHER workspace must be a 400, not a silently empty list.
        // It is created inside its own workspace rather than by moving this one's row: labels are
        // unique per (workspace_id, name), so re-homing an existing name would trip that index instead
        // of the rule under test.
        var otherWorkspace = await scenario.CreateWorkspaceAsync(me, "Workspace nhãn khác");
        var (foreignLabelId, _) = await SeedLabelsAsync(scenario, otherWorkspace.Id, me, "Nhãn ngoài", "Nhãn ngoài B");

        var badLabel = await client.GetAsync(
            $"/api/workspaces/{workspace.Id}/tasks/search?labelIds={foreignLabelId.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, badLabel.StatusCode);

        // Eleven ids is a UI mistake, not a search.
        var many = string.Join(',', Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()));
        var tooMany = await client.GetAsync($"/api/workspaces/{workspace.Id}/tasks/search?labelIds={many}");
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
    }

    [Theory]
    [InlineData("urgent", HttpStatusCode.OK)]
    [InlineData("Urgent", HttpStatusCode.OK)]
    [InlineData("1", HttpStatusCode.BadRequest)]
    [InlineData("99", HttpStatusCode.BadRequest)]
    [InlineData("Boss", HttpStatusCode.BadRequest)]
    public async Task PriorityFilter_AcceptsOnlyTheFourStoredNames(string value, HttpStatusCode expected)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, board, todo, _) = await SeedAsync(scenario);

        await SeedTaskAsync(
            scenario, board, todo, "Khẩn cấp", me.Id,
            priority: TaskPriority.Urgent,
            updatedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        using var client = await scenario.AsUserAsync(me);
        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}/tasks/search?priority={value}");

        Assert.Equal(expected, response.StatusCode);

        if (expected == HttpStatusCode.OK)
        {
            var result = await response.Content.ReadFromJsonAsync<SearchDto>();
            Assert.Single(result!.Items);
            Assert.Equal("Urgent", result.Items[0].Task.Priority);
        }
    }

    [Fact]
    public async Task DueDateRange_FiltersInclusively_AndRejectsAnInvertedRange()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, board, todo, _) = await SeedAsync(scenario);

        var day1 = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var day2 = new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero);
        var day3 = new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero);

        await SeedTaskAsync(scenario, board, todo, "Hạn ngày 1", me.Id, dueDate: day1, updatedAt: day1);
        await SeedTaskAsync(scenario, board, todo, "Hạn ngày 2", me.Id, dueDate: day2, updatedAt: day2);
        await SeedTaskAsync(scenario, board, todo, "Hạn ngày 3", me.Id, dueDate: day3, updatedAt: day3);
        await SeedTaskAsync(scenario, board, todo, "Không hạn", me.Id, dueDate: null, updatedAt: day3);

        using var client = await scenario.AsUserAsync(me);

        var range = await SearchRequiredAsync(client, workspace.Id, query: null, dueFrom: day1, dueTo: day2);
        Assert.Equal(2, range.Items.Count);

        // The bounds are inclusive on both ends.
        var singleDay = await SearchRequiredAsync(client, workspace.Id, query: null, dueFrom: day2, dueTo: day2);
        Assert.Single(singleDay.Items);
        Assert.Equal("Hạn ngày 2", singleDay.Items[0].Task.Title);

        var inverted = await client.GetAsync(
            $"/api/workspaces/{workspace.Id}/tasks/search?dueFrom={day3:O}&dueTo={day1:O}");
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
    }

    [Fact]
    public async Task DueDateWithANonUtcOffset_IsNormalisedInsteadOfFailing()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, board, todo, _) = await SeedAsync(scenario);

        var due = new DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero);
        await SeedTaskAsync(scenario, board, todo, "Hạn UTC", me.Id, dueDate: due, updatedAt: due);

        using var client = await scenario.AsUserAsync(me);

        // 16:00+07:00 is the same instant as 09:00Z. Phase 10's bug turned an offset into a 500; the
        // search endpoint must normalize instead.
        var response = await client.GetAsync(
            $"/api/workspaces/{workspace.Id}/tasks/search?dueFrom=2026-05-10T16:00:00%2B07:00&dueTo=2026-05-10T16:00:01%2B07:00");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SearchDto>();
        Assert.Single(result!.Items);
    }

    [Fact]
    public async Task OverdueFilter_KeepsOnlyOpenTasksPastTheirDueDate()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, board, todo, done) = await SeedAsync(scenario);
        var now = DateTimeOffset.UtcNow;

        await SeedTaskAsync(scenario, board, todo, "Quá hạn", me.Id, dueDate: now.AddDays(-1), updatedAt: now.AddDays(-1));
        await SeedTaskAsync(scenario, board, todo, "Còn hạn", me.Id, dueDate: now.AddDays(1), updatedAt: now.AddDays(-2));
        await SeedTaskAsync(scenario, board, todo, "Không hạn", me.Id, dueDate: null, updatedAt: now.AddDays(-3));
        await SeedTaskAsync(scenario, board, done, "Xong rồi", me.Id, dueDate: now.AddDays(-5), updatedAt: now.AddDays(-4));

        using var client = await scenario.AsUserAsync(me);
        var result = await SearchRequiredAsync(client, workspace.Id, query: null, overdue: true);

        Assert.Single(result.Items);
        Assert.Equal("Quá hạn", result.Items[0].Task.Title);
    }

    [Fact]
    public async Task IncludeDone_DefaultsToTrue_AndFalseHidesFinishedWork()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, board, todo, done) = await SeedAsync(scenario);

        await SeedTaskAsync(scenario, board, todo, "Đang mở", me.Id, updatedAt: new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));
        await SeedTaskAsync(scenario, board, done, "Trong cột done", me.Id, updatedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        using var client = await scenario.AsUserAsync(me);

        var byDefault = await SearchRequiredAsync(client, workspace.Id, query: null);
        Assert.Equal(2, byDefault.Items.Count);

        var openOnly = await SearchRequiredAsync(client, workspace.Id, query: null, includeDone: false);
        Assert.Single(openOnly.Items);
        Assert.Equal("Đang mở", openOnly.Items[0].Task.Title);
    }

    // ---- isolation ---------------------------------------------------------

    [Fact]
    public async Task SoftDeletedTasksAndBoards_AreInvisible()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, board, todo, _) = await SeedAsync(scenario);

        var live = await SeedTaskAsync(scenario, board, todo, "Còn sống", me.Id, updatedAt: new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero));
        var softDeleted = await SeedTaskAsync(scenario, board, todo, "Đã xoá mềm", me.Id, updatedAt: new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));

        var (deadBoard, deadTodo, _) = await AddBoardAsync(scenario, workspace.Id, "Board đã xoá", me);
        await SeedTaskAsync(scenario, deadBoard, deadTodo, "Trên board đã xoá", me.Id, updatedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        await using (var db = scenario.NewDbContext())
        {
            var task = await db.Tasks.FirstAsync(t => t.Id == softDeleted.Id);
            task.DeletedAt = DateTimeOffset.UtcNow;

            var boardRow = await db.Boards.FirstAsync(b => b.Id == deadBoard.Id);
            boardRow.DeletedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync();
        }

        using var client = await scenario.AsUserAsync(me);
        var result = await SearchRequiredAsync(client, workspace.Id, query: null);

        Assert.Single(result.Items);
        Assert.Equal("Còn sống", result.Items[0].Task.Title);
    }

    [Fact]
    public async Task AnOutsider_GetsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (_, workspace, _, _, _) = await SeedAsync(scenario);
        var outsider = await scenario.CreateUserAsync("Người ngoài", "search.noaccess@example.test");

        using var client = await scenario.AsUserAsync(outsider);
        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}/tasks/search");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- keyset paging -----------------------------------------------------

    [Fact]
    public async Task KeysetPaging_CoversEveryRowExactlyOnce_AndSurvivesInsertsBetweenPages()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, board, todo, _) = await SeedAsync(scenario);

        var baseTime = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var titles = new List<string>();
        for (var i = 0; i < 30; i++)
        {
            var title = $"Thẻ {i:D2}";
            titles.Add(title);
            await SeedTaskAsync(scenario, board, todo, title, me.Id, updatedAt: baseTime.AddMinutes(i));
        }

        using var client = await scenario.AsUserAsync(me);

        var first = await SearchRequiredAsync(client, workspace.Id, query: null, take: 25);
        Assert.Equal(25, first.Items.Count);
        Assert.True(first.HasMore);
        Assert.NotNull(first.NextCursor);

        // A row created between the two pages is NEWER than the cursor, so it must not shift page 2
        // (which is exactly what OFFSET paging would get wrong).
        await SeedTaskAsync(
            scenario, board, todo, "Chèn giữa hai trang", me.Id,
            updatedAt: baseTime.AddMinutes(100));

        var second = await SearchAsyncWithCursor(client, workspace.Id, first.NextCursor!);
        Assert.NotNull(second);
        Assert.Equal(5, second.Items.Count);
        Assert.False(second.HasMore);
        Assert.Null(second.NextCursor);

        var seen = first.Items.Concat(second.Items).Select(i => i.Task.Title).ToList();
        Assert.Equal(titles.Count, seen.Count);
        Assert.Equal(titles.Count, seen.Distinct().Count());

        // Newest first across the whole result set.
        Assert.Equal("Thẻ 29", seen[0]);
        Assert.Equal("Thẻ 00", seen[^1]);
    }

    [Fact]
    public async Task ACorruptCursor_IsRejectedWithBadRequest()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, _, _, _) = await SeedAsync(scenario);

        using var client = await scenario.AsUserAsync(me);

        foreach (var cursor in new[] { "abc", "|", "|not-a-guid", "not-a-date|11111111-1111-1111-1111-111111111111" })
        {
            var response = await client.GetAsync(
                $"/api/workspaces/{workspace.Id}/tasks/search?cursor={Uri.EscapeDataString(cursor)}");

            // Never 500, and never a silent "start from the beginning" (which loops the UI forever).
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    // ---- shape -------------------------------------------------------------

    [Fact]
    public async Task Results_CarryBoardAndColumnContextAndTheSharedTaskShape()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, board, todo, _) = await SeedAsync(scenario);
        var label = await SeedLabelsAsync(scenario, workspace.Id, me, "Nhãn tìm kiếm");

        var task = await SeedTaskAsync(
            scenario, board, todo, "Thẻ có ngữ cảnh", me.Id,
            assigneeId: me.Id,
            priority: TaskPriority.High,
            updatedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
        await AttachLabelAsync(scenario, task.Id, label.Item1.Id);

        await using (var db = scenario.NewDbContext())
        {
            db.TaskComments.Add(new TaskComment { Id = Guid.NewGuid(), TaskId = task.Id, AuthorId = me.Id, Content = "một bình luận" });
            await db.SaveChangesAsync();
        }

        using var client = await scenario.AsUserAsync(me);
        var result = await SearchRequiredAsync(client, workspace.Id, query: "ngữ cảnh");

        var item = Assert.Single(result.Items);
        Assert.Equal("Board chính", item.BoardName);
        Assert.Equal("Todo", item.ColumnName);
        Assert.False(item.IsDoneColumn);

        // Labels and the comment count come from the SAME batch loaders the Kanban board uses, so a
        // result card can never disagree with the card on the board (§P1).
        Assert.Equal("Nhãn tìm kiếm", Assert.Single(item.Task.Labels).Name);
        Assert.Equal(1, item.Task.CommentCount);
        Assert.Equal(me.Id, item.Task.AssigneeId);
        Assert.Equal("High", item.Task.Priority);
        Assert.Null(item.Task.ActiveAgentRunId);
    }

    [Fact]
    public async Task Results_MarkTheDoneColumn()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (me, workspace, board, _, done) = await SeedAsync(scenario);

        await SeedTaskAsync(scenario, board, done, "Ở cột done", me.Id, updatedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

        using var client = await scenario.AsUserAsync(me);
        var result = await SearchRequiredAsync(client, workspace.Id, query: null);

        var item = Assert.Single(result.Items);
        Assert.True(item.IsDoneColumn);
        Assert.Equal("Done", item.ColumnName);
    }

    // ---- helpers -----------------------------------------------------------

    private static Task<SearchDto?> SearchAsync(
        TestHttpClient client,
        Guid workspaceId,
        string? query,
        Guid? boardId = null,
        Guid? assigneeId = null,
        bool unassigned = false,
        IReadOnlyList<Guid>? labelIds = null,
        DateTimeOffset? dueFrom = null,
        DateTimeOffset? dueTo = null,
        bool overdue = false,
        bool? includeDone = null,
        int? take = null)
    {
        var url = BuildUrl(workspaceId, query, boardId, assigneeId, unassigned, labelIds, dueFrom, dueTo, overdue, includeDone, take, cursor: null);
        return client.GetJsonAsync<SearchDto>(url);
    }

    /// <summary>
    /// Same as <see cref="SearchAsync"/>, but fails loudly instead of returning null.
    /// <para>
    /// <c>GetJsonAsync&lt;T&gt;</c> is nullable, so every <c>result.Items</c> would otherwise need a
    /// null-forgiving operator. A 200 with a body the test cannot read is a broken test, not a passing
    /// one — this throws where the failure is legible.
    /// </para>
    /// </summary>
    private static async Task<SearchDto> SearchRequiredAsync(
        TestHttpClient client,
        Guid workspaceId,
        string? query,
        Guid? boardId = null,
        Guid? assigneeId = null,
        bool unassigned = false,
        IReadOnlyList<Guid>? labelIds = null,
        DateTimeOffset? dueFrom = null,
        DateTimeOffset? dueTo = null,
        bool overdue = false,
        bool? includeDone = null,
        int? take = null)
    {
        var result = await SearchAsync(
            client, workspaceId, query, boardId, assigneeId, unassigned, labelIds, dueFrom, dueTo, overdue, includeDone, take);

        return result ?? throw new InvalidOperationException(
            "GET /tasks/search answered without a JSON body the test can read.");
    }

    private static Task<SearchDto?> SearchAsyncWithCursor(TestHttpClient client, Guid workspaceId, string cursor)
    {
        var url = BuildUrl(workspaceId, query: null, boardId: null, assigneeId: null, unassigned: false, labelIds: null, dueFrom: null, dueTo: null, overdue: false, includeDone: null, take: null, cursor: cursor);
        return client.GetJsonAsync<SearchDto>(url);
    }

    private static string BuildUrl(
        Guid workspaceId,
        string? query,
        Guid? boardId,
        Guid? assigneeId,
        bool unassigned,
        IReadOnlyList<Guid>? labelIds,
        DateTimeOffset? dueFrom,
        DateTimeOffset? dueTo,
        bool overdue,
        bool? includeDone,
        int? take,
        string? cursor)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(query))
        {
            parts.Add($"q={Uri.EscapeDataString(query)}");
        }

        if (boardId.HasValue)
        {
            parts.Add($"boardId={boardId.Value}");
        }

        if (assigneeId.HasValue)
        {
            parts.Add($"assigneeId={assigneeId.Value}");
        }

        if (unassigned)
        {
            parts.Add("unassigned=true");
        }

        if (labelIds is { Count: > 0 })
        {
            parts.Add($"labelIds={string.Join(',', labelIds)}");
        }

        if (dueFrom.HasValue)
        {
            parts.Add($"dueFrom={Uri.EscapeDataString(dueFrom.Value.ToString("O"))}");
        }

        if (dueTo.HasValue)
        {
            parts.Add($"dueTo={Uri.EscapeDataString(dueTo.Value.ToString("O"))}");
        }

        if (overdue)
        {
            parts.Add("overdue=true");
        }

        if (includeDone.HasValue)
        {
            parts.Add($"includeDone={(includeDone.Value ? "true" : "false")}");
        }

        if (take.HasValue)
        {
            parts.Add($"take={take.Value}");
        }

        if (!string.IsNullOrEmpty(cursor))
        {
            parts.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        var queryString = parts.Count > 0 ? "?" + string.Join('&', parts) : string.Empty;
        return $"/api/workspaces/{workspaceId}/tasks/search{queryString}";
    }

    private static async Task<(ApplicationUser Me, Workspace Workspace, Board Board, BoardColumn Todo, BoardColumn Done)>
        SeedAsync(TestScenario scenario)
    {
        var me = await scenario.CreateUserAsync("Người tìm kiếm");
        var workspace = await scenario.CreateWorkspaceAsync(me, "Workspace tìm kiếm");
        var (board, todo, done) = await scenario.CreateBoardAsync(workspace, "Board chính");
        return (me, workspace, board, todo, done);
    }

    private static async Task<(Board Board, BoardColumn Todo, BoardColumn Done)> AddBoardAsync(
        TestScenario scenario, Guid workspaceId, string name, ApplicationUser member)
    {
        await using var db = scenario.NewDbContext();

        var board = new Board { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Name = name };
        var todo = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Todo", Position = 0, IsDone = false };
        var done = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Done", Position = 1, IsDone = true };

        db.Boards.Add(board);
        db.BoardColumns.AddRange(todo, done);
        await db.SaveChangesAsync();

        return (board, todo, done);
    }

    private static async Task AddMemberAsync(
        TestScenario scenario, Guid workspaceId, ApplicationUser user, WorkspaceRole role)
    {
        await using var db = scenario.NewDbContext();
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = user.Id, Role = role });
        await db.SaveChangesAsync();
    }

    private static async Task<(Label A, Label B)> SeedLabelsAsync(
        TestScenario scenario, Guid workspaceId, ApplicationUser owner, string nameA, string nameB = "Nhãn B")
    {
        await using var db = scenario.NewDbContext();

        var a = new Label { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Name = nameA, Color = "#6366f1" };
        var b = new Label { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Name = nameB, Color = "#f97316" };

        db.Labels.AddRange(a, b);
        await db.SaveChangesAsync();

        return (a, b);
    }

    private static async Task AttachLabelAsync(TestScenario scenario, Guid taskId, Guid labelId)
    {
        await using var db = scenario.NewDbContext();
        db.TaskLabels.Add(new TaskLabel { TaskId = taskId, LabelId = labelId });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Inserts a task with explicit <c>created_at</c>/<c>updated_at</c>.
    /// <para>
    /// The post-insert UPDATE exists because <c>TeamNexusDbContext.SaveChanges</c> stamps both audit
    /// columns with the wall clock for every added <c>IAuditableEntity</c> — a keyset test that cannot
    /// control <c>updated_at</c> cannot assert an order at all.
    /// </para>
    /// <para>
    /// <paramref name="createdBy"/> is explicit because <c>tasks.created_by</c> is a Restrict FK: an
    /// unassigned task still needs a real author row, and defaulting it to <c>Guid.Empty</c> would fail
    /// the foreign key at insert time.
    /// </para>
    /// </summary>
    private static async Task<BoardTask> SeedTaskAsync(
        TestScenario scenario,
        Board board,
        BoardColumn column,
        string title,
        Guid createdBy,
        Guid? assigneeId = null,
        DateTimeOffset? dueDate = null,
        TaskPriority? priority = null,
        string? description = null,
        DateTimeOffset? updatedAt = null)
    {
        await using var db = scenario.NewDbContext();

        var stamp = updatedAt ?? DateTimeOffset.UtcNow;

        var task = new BoardTask
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            ColumnId = column.Id,
            Title = title,
            Description = description,
            Position = 0,
            CreatedBy = createdBy,
            AssigneeId = assigneeId,
            DueDate = dueDate,
            Priority = priority,
            CompletedAt = column.IsDone ? stamp : null,
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE tasks SET created_at = {0}, updated_at = {0} WHERE id = {1}",
            stamp,
            task.Id);

        task.CreatedAt = stamp;
        task.UpdatedAt = stamp;
        return task;
    }

    // ---- response shapes ---------------------------------------------------

    private sealed record SearchDto(
        IReadOnlyList<SearchItemDto> Items,
        string? NextCursor,
        bool HasMore,
        bool HasQuery);

    private sealed record SearchItemDto(TaskDto Task, string BoardName, string ColumnName, bool IsDoneColumn);

    private sealed record TaskDto(
        Guid Id,
        Guid BoardId,
        Guid ColumnId,
        string Title,
        string? Description,
        Guid? AssigneeId,
        string? AssigneeName,
        DateTimeOffset? DueDate,
        string? Priority,
        IReadOnlyList<LabelDto> Labels,
        int CommentCount,
        bool AssigneeIsAiAgent,
        Guid? ActiveAgentRunId);

    private sealed record LabelDto(Guid Id, Guid WorkspaceId, string Name, string Color, DateTimeOffset CreatedAt);
}
