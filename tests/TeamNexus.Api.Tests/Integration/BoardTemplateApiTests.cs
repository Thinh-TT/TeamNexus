using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 14 §4 — the AI Board Template (BT-GEN-1…BT-REGRESS-1).
/// <para>
/// The AI is scripted (<see cref="ScriptedAiProvider"/>) so the suite asserts the <b>server's</b>
/// behaviour: normalization of an untrusted answer, the "generate writes nothing / confirm writes only a
/// log / approve creates the board" ladder, and the soft-delete Undo.
/// </para>
/// </summary>
public sealed class BoardTemplateApiTests : IClassFixture<DatabaseFixture>
{
    /// <summary>
    /// A well-formed proposal with everything the normalizer has to survive: five columns (one of them
    /// duplicate by name), a task pointing at a column that does not exist, an invalid priority, an
    /// unknown label and an assignee who is not a member.
    /// </summary>
    private const string ProposalJson = """
        {
          "summary": "Bảng cho dự án di trú",
          "boardName": "Di trú dữ liệu 2026",
          "boardDescription": "Kế hoạch di trú lên PostgreSQL 18",
          "columns": [
            { "name": "Cần làm", "isDone": false },
            { "name": "Đang làm", "isDone": false },
            { "name": "Chờ duyệt", "isDone": false },
            { "name": "Đang làm", "isDone": false },
            { "name": "Hoàn tất", "isDone": true }
          ],
          "tasks": [
            { "title": "Khảo sát dữ liệu hiện có", "priority": "High", "columnName": "Cần làm", "labels": ["backend"] },
            { "title": "Viết script di trú", "priority": "Medium", "columnName": "Đang làm", "labels": [] },
            { "title": "Chạy thử trên staging", "priority": "urgent", "columnName": "Chờ duyệt", "labels": ["backend"] },
            { "title": "Kiểm tra tính toàn vẹn", "priority": "Không hợp lệ", "columnName": "Không tồn tại", "labels": [] },
            { "title": "Bàn giao tài liệu", "priority": null, "columnName": "Hoàn tất", "labels": [] },
            { "title": "Tập huấn cho nhóm", "priority": "Low", "columnName": "Cần làm", "labels": [], "suggestedAssignee": "Người Không Tồn Tại" },
            { "title": "Rà soát chỉ mục", "priority": "High", "columnName": "Đang làm", "labels": [] },
            { "title": "Đo hiệu năng", "priority": "High", "columnName": "Cần làm", "labels": [] },
            { "title": "Lập kế hoạch rollback", "priority": "High", "columnName": "Cần làm", "labels": [] },
            { "title": "Họp tổng kết", "priority": "Low", "columnName": "Hoàn tất", "labels": [] },
            { "title": "Task vượt trần, phải bị cắt", "priority": "Low", "columnName": "Cần làm", "labels": [] }
          ]
        }
        """;

    private readonly DatabaseFixture _database;

    public BoardTemplateApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- BT-GEN: generate never writes -------------------------------------

    [Fact]
    public async Task BT_GEN_1_ManagerGetsAProposalWithinTheDocumentedBounds()
    {
        await using var scenario = await _database.CreateScenarioAsync(new ScriptedAiProvider { Content = ProposalJson });
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.Manager);

        var response = await GenerateAsync(client, seed.Workspace.Id, "Di trú dữ liệu lên PostgreSQL 18");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var proposal = await response.Content.ReadFromJsonAsync<BoardTemplateProposal>();
        Assert.NotNull(proposal);

        // Cột: 2..6, đã dedupe tên trùng, và ĐÚNG MỘT cột isDone (decision D13).
        Assert.InRange(proposal!.Columns.Count, 2, 6);
        Assert.Equal(4, proposal.Columns.Count); // 5 cột - 1 trùng tên
        Assert.Single(proposal.Columns, c => c.IsDone);
        Assert.Equal("Hoàn tất", proposal.Columns.Single(c => c.IsDone).Name);
        Assert.Equal(
            proposal.Columns.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            proposal.Columns.Count);

        // Task: 5..10, title bắt buộc, priority chuẩn hoá, columnName luôn trỏ tới một cột có thật.
        Assert.InRange(proposal.Tasks.Count, 5, 10);
        Assert.All(proposal.Tasks, t => Assert.False(string.IsNullOrWhiteSpace(t.Title)));
        Assert.All(
            proposal.Tasks,
            t => Assert.Contains(t.ColumnName, proposal.Columns.Select(c => c.Name)));

        // `urgent` → `Urgent` (chuẩn hoá chữ hoa/thường); giá trị rác → null.
        Assert.Equal("Urgent", proposal.Tasks.Single(t => t.Title == "Chạy thử trên staging").Priority);
        Assert.Null(proposal.Tasks.Single(t => t.Title == "Kiểm tra tính toàn vẹn").Priority);

        // `columnName` không tồn tại ⇒ rơi vào cột KHÔNG done đầu tiên, KHÔNG bị bỏ.
        Assert.Equal(
            "Cần làm",
            proposal.Tasks.Single(t => t.Title == "Kiểm tra tính toàn vẹn").ColumnName);

        // Người không phải thành viên ⇒ matched = false và userId = null (không gán bừa).
        var unmatched = proposal.Tasks.Single(t => t.Title == "Tập huấn cho nhóm").Assignee;
        Assert.NotNull(unmatched);
        Assert.False(unmatched!.Matched);
        Assert.Null(unmatched.UserId);

        // Bảng và tóm tắt có thật, không rỗng.
        Assert.Equal("Di trú dữ liệu 2026", proposal.BoardName);
        Assert.False(string.IsNullOrWhiteSpace(proposal.Summary));
    }

    [Fact]
    public async Task BT_GEN_2_MemberIsForbidden()
    {
        await using var scenario = await _database.CreateScenarioAsync(new ScriptedAiProvider { Content = ProposalJson });

        // Manager tạo workspace, rồi một Member thuần được thêm vào và tự gọi endpoint.
        var seed = await SeedAsync(scenario);
        var member = await scenario.CreateUserAsync("Thành viên thường");
        await AddMemberAsync(scenario, seed.Workspace.Id, member, WorkspaceRole.Member);

        using var client = await scenario.AsUserAsync(member);
        var response = await GenerateAsync(client, seed.Workspace.Id, "Tôi không phải quản lý");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BT_GEN_3_UnknownWorkspaceIs404()
    {
        await using var scenario = await _database.CreateScenarioAsync(new ScriptedAiProvider { Content = ProposalJson });
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.Manager);

        var response = await GenerateAsync(client, Guid.NewGuid(), "Workspace không tồn tại");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BT_GEN_4_DescriptionMustBeWithinBounds()
    {
        await using var scenario = await _database.CreateScenarioAsync(new ScriptedAiProvider { Content = ProposalJson });
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.Manager);

        var empty = await GenerateAsync(client, seed.Workspace.Id, "   ");
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var tooLong = await GenerateAsync(client, seed.Workspace.Id, new string('x', 4001));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        // 4000 ký tự (đúng biên) phải được chấp nhận ⇒ ngưỡng là "<=".
        var exactly = await GenerateAsync(client, seed.Workspace.Id, new string('y', 4000));
        Assert.Equal(HttpStatusCode.OK, exactly.StatusCode);
    }

    [Fact]
    public async Task BT_GEN_5_GeneratingWritesNothingAtAll()
    {
        await using var scenario = await _database.CreateScenarioAsync(new ScriptedAiProvider { Content = ProposalJson });
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.Manager);

        await using var before = scenario.NewDbContext();
        var boardsBefore = await before.Boards.CountAsync();
        var columnsBefore = await before.BoardColumns.CountAsync();
        var tasksBefore = await before.Tasks.CountAsync();
        var logsBefore = await before.AiActionLogs.CountAsync();

        var response = await GenerateAsync(client, seed.Workspace.Id, "Đề xuất bảng nhưng đừng ghi gì");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var after = scenario.NewDbContext();
        Assert.Equal(boardsBefore, await after.Boards.CountAsync());
        Assert.Equal(columnsBefore, await after.BoardColumns.CountAsync());
        Assert.Equal(tasksBefore, await after.Tasks.CountAsync());
        Assert.Equal(logsBefore, await after.AiActionLogs.CountAsync());
    }

    [Fact]
    public async Task BT_GEN_6_ProposalWithoutEnoughColumnsIsRejected()
    {
        // AI trả về MỘT cột: không thể dựng một luồng công việc ⇒ 400, không có bảng nửa vời.
        var scripted = new ScriptedAiProvider
        {
            Content = """
                { "boardName": "Một cột", "columns": [ { "name": "Cần làm", "isDone": false } ],
                  "tasks": [ { "title": "Việc A", "columnName": "Cần làm" } ] }
                """,
        };

        await using var scenario = await _database.CreateScenarioAsync(scripted);
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.Manager);

        var response = await GenerateAsync(client, seed.Workspace.Id, "Mô tả dự án chỉ có một bước");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("2 cột", await ErrorAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BT_GEN_7_WhenTheModelMarksNoDoneColumnTheLastOneIsPinned()
    {
        var scripted = new ScriptedAiProvider
        {
            Content = """
                {
                  "boardName": "Không đánh dấu done",
                  "columns": [ { "name": "Cần làm" }, { "name": "Đang làm" }, { "name": "Xong" } ],
                  "tasks": [ { "title": "Việc A", "columnName": "Cần làm" },
                             { "title": "Việc B", "columnName": "Đang làm" },
                             { "title": "Việc C", "columnName": "Xong" },
                             { "title": "Việc D", "columnName": "Cần làm" },
                             { "title": "Việc E", "columnName": "Cần làm" } ]
                }
                """,
        };

        await using var scenario = await _database.CreateScenarioAsync(scripted);
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.Manager);

        var proposal = await (await GenerateAsync(client, seed.Workspace.Id, "Mô tả đủ dài cho dự án"))
            .Content.ReadFromJsonAsync<BoardTemplateProposal>();

        Assert.NotNull(proposal);

        // Không cột nào được đánh dấu ⇒ cột CUỐI (nơi luồng công việc kết thúc) được pin, và vẫn đúng MỘT.
        Assert.Single(proposal!.Columns, c => c.IsDone);
        Assert.True(proposal.Columns[^1].IsDone);
    }

    [Fact]
    public async Task BT_GEN_8_MalformedJsonIsRetriedOnceAndThenSurfacesAs502()
    {
        // ScriptedAiProvider luôn trả cùng một nội dung ⇒ lần thử lại cũng hỏng ⇒ 502 (không phải 500).
        var scripted = new ScriptedAiProvider { Content = "không phải JSON" };

        await using var scenario = await _database.CreateScenarioAsync(scripted);
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.Manager);

        var response = await GenerateAsync(client, seed.Workspace.Id, "Mô tả dự án hợp lệ nhưng AI trả rác");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        // Đã THỬ LẠI: đúng 2 lượt gọi provider (1 lần + 1 lần chặt hơn).
        Assert.Equal(2, scripted.CallCount);

        await using var db = scenario.NewDbContext();
        Assert.Equal(0, await db.AiActionLogs.CountAsync());

        // Không có bảng nào được tạo: chỉ còn bảng seed ban đầu (TestScenario đặt tên "Board Test").
        var names = await db.Boards.IgnoreQueryFilters().Select(b => b.Name).ToListAsync();
        Assert.True(
            names.Count == 1 && names[0] == "Board Test",
            "Không được có bảng nào khác ngoài bảng seed. Thực tế: " + string.Join(", ", names));
    }

    // ---- BT-CONFIRM: the log row ------------------------------------------

    [Fact]
    public async Task BT_CONFIRM_1_ConfirmCreatesAPendingWorkspaceScopedActionAndNoBoard()
    {
        await using var scenario = await _database.CreateScenarioAsync(new ScriptedAiProvider { Content = ProposalJson });
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.Manager);

        var proposal = await GenerateProposalAsync(client, seed.Workspace.Id);
        var response = await ConfirmAsync(client, seed.Workspace.Id, proposal);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var log = await response.Content.ReadFromJsonAsync<AiActionLogDto>();
        Assert.NotNull(log);
        Assert.Equal("CreateBoardFromTemplate", log!.Action);
        Assert.Equal("Workspace", log.EntityType);
        Assert.Equal(seed.Workspace.Id, log.EntityId);
        Assert.Equal("Pending", log.Status);

        // PENDING KHÔNG TẠO BẢNG.
        await using var db = scenario.NewDbContext();
        Assert.Equal(1, await db.Boards.CountAsync());
        Assert.Equal(1, await db.AiActionLogs.CountAsync());
    }

    [Fact]
    public async Task BT_CONFIRM_2_ConfirmationIsManagerOnly()
    {
        await using var scenario = await _database.CreateScenarioAsync(new ScriptedAiProvider { Content = ProposalJson });
        var seed = await SeedAsync(scenario);
        using var manager = await scenario.AsUserAsync(seed.Manager);

        var proposal = await GenerateProposalAsync(manager, seed.Workspace.Id);

        var member = await scenario.CreateUserAsync("Thành viên xác nhận");
        await AddMemberAsync(scenario, seed.Workspace.Id, member, WorkspaceRole.Member);

        using var client = await scenario.AsUserAsync(member);
        var response = await ConfirmAsync(client, seed.Workspace.Id, proposal);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var db = scenario.NewDbContext();
        Assert.Equal(0, await db.AiActionLogs.CountAsync());
    }

    [Fact]
    public async Task BT_CONFIRM_3_ATamperedProposalCannotCreateAMalformedBoard()
    {
        await using var scenario = await _database.CreateScenarioAsync(new ScriptedAiProvider { Content = ProposalJson });
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.Manager);

        var proposal = await GenerateProposalAsync(client, seed.Workspace.Id);

        // Người dùng (hoặc một client bị sửa) gửi lên đề xuất đã bị phá: 0 cột.
        var broken = proposal with { Columns = [] };
        var response = await ConfirmAsync(client, seed.Workspace.Id, broken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var db = scenario.NewDbContext();
        Assert.Equal(0, await db.AiActionLogs.CountAsync());
    }

    [Fact]
    public async Task BT_CONFIRM_4_ATamperedProposalCannotDropTheSingleDoneColumn()
    {
        await using var scenario = await _database.CreateScenarioAsync(new ScriptedAiProvider { Content = ProposalJson });
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.Manager);

        var proposal = await GenerateProposalAsync(client, seed.Workspace.Id);

        // Client cố tình đặt isDone = false cho MỌI cột ⇒ server phải pin lại đúng một cột khi approve,
        // nếu không board sẽ không có cột "done" và mọi số liệu báo cáo sai.
        var tampered = proposal with
        {
            Columns = proposal.Columns.Select(c => c with { IsDone = false }).ToList(),
        };

        var log = await (await ConfirmAsync(client, seed.Workspace.Id, tampered))
            .Content.ReadFromJsonAsync<AiActionLogDto>();

        using var approve = await client.PostJsonAsync($"/api/ai-actions/{log!.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        await using var db = scenario.NewDbContext();
        var newBoard = await db.Boards.SingleAsync(b => b.Name == "Di trú dữ liệu 2026");
        var doneColumns = await db.BoardColumns.CountAsync(c => c.BoardId == newBoard.Id && c.IsDone);

        Assert.Equal(1, doneColumns);
    }

    // ---- BT-APPROVE / BT-UNDO: the real writes ----------------------------

    [Fact]
    public async Task BT_APPROVE_1_ApprovingCreatesExactlyOneBoardWithItsColumnsAndTasks()
    {
        await using var scenario = await _database.CreateScenarioAsync(new ScriptedAiProvider { Content = ProposalJson });
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.Manager);

        var proposal = await GenerateProposalAsync(client, seed.Workspace.Id);
        var log = await (await ConfirmAsync(client, seed.Workspace.Id, proposal))
            .Content.ReadFromJsonAsync<AiActionLogDto>();

        var approve = await client.PostJsonAsync($"/api/ai-actions/{log!.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        await using var db = scenario.NewDbContext();
        var board = await db.Boards.SingleAsync(b => b.Name == "Di trú dữ liệu 2026");

        var columns = await db.BoardColumns
            .Where(c => c.BoardId == board.Id)
            .OrderBy(c => c.Position)
            .ToListAsync();

        // Đúng số cột của đề xuất, đúng thứ tự, và ĐÚNG MỘT cột done.
        Assert.Equal(proposal.Columns.Count, columns.Count);
        Assert.Equal(proposal.Columns.Select(c => c.Name), columns.Select(c => c.Name));
        Assert.Equal(1, columns.Count(c => c.IsDone));
        Assert.Equal(proposal.Columns.Single(c => c.IsDone).Name, columns.Single(c => c.IsDone).Name);

        var tasks = await db.Tasks.Where(t => t.BoardId == board.Id).ToListAsync();
        Assert.Equal(proposal.Tasks.Count, tasks.Count);
        Assert.All(tasks, t => Assert.InRange(t.Position, 0, int.MaxValue));

        // Mỗi task nằm trong ĐÚNG cột mà đề xuất chỉ định (resolve theo tên).
        foreach (var proposed in proposal.Tasks)
        {
            var columnId = columns.Single(c => c.Name == proposed.ColumnName).Id;
            Assert.Contains(tasks, t => t.Title == proposed.Title && t.ColumnId == columnId);
        }

        // Priority đã được service parse (chuỗi) chứ không lưu nguyên văn của AI.
        var urgent = tasks.Single(t => t.Title == "Chạy thử trên staging");
        Assert.Equal(TaskPriority.Urgent, urgent.Priority);
    }

    [Fact]
    public async Task BT_APPROVE_2_AppliedSnapshotRecordsEveryCreatedId()
    {
        await using var scenario = await _database.CreateScenarioAsync(new ScriptedAiProvider { Content = ProposalJson });
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.Manager);

        var proposal = await GenerateProposalAsync(client, seed.Workspace.Id);
        var log = await (await ConfirmAsync(client, seed.Workspace.Id, proposal))
            .Content.ReadFromJsonAsync<AiActionLogDto>();

        await client.PostJsonAsync($"/api/ai-actions/{log!.Id}/approve", new { });

        var detail = await client.GetJsonAsync<AiActionLogDetailDto>($"/api/ai-actions/{log.Id}");

        Assert.NotNull(detail);
        Assert.Equal("Approved", detail!.Status);
        Assert.Equal(proposal.Tasks.Count, detail.CreatedTaskIds.Count);

        // `createdColumnIds` là field MỚI của Giai đoạn 14 (P3): Undo dựa vào nó.
        Assert.NotNull(detail.AppliedSnapshot);
        Assert.True(detail.AppliedSnapshot!.Value.TryGetProperty("createdColumnIds", out var columnIds));
        Assert.Equal(proposal.Columns.Count, columnIds.GetArrayLength());

        // `createdBoardId` cũng phải có — đó là thứ Undo đọc để soft-delete.
        Assert.True(detail.AppliedSnapshot.Value.TryGetProperty("createdBoardId", out var boardId));
        Assert.NotEqual(Guid.Empty, boardId.GetGuid());
    }

    [Fact]
    public async Task BT_UNDO_1_UndoSoftDeletesTheBoardAndItDisappearsFromEverywhere()
    {
        await using var scenario = await _database.CreateScenarioAsync(new ScriptedAiProvider { Content = ProposalJson });
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.Manager);

        var proposal = await GenerateProposalAsync(client, seed.Workspace.Id);
        var log = await (await ConfirmAsync(client, seed.Workspace.Id, proposal))
            .Content.ReadFromJsonAsync<AiActionLogDto>();

        await client.PostJsonAsync($"/api/ai-actions/{log!.Id}/approve", new { });

        await using (var created = scenario.NewDbContext())
        {
            Assert.Equal(1, await created.Boards.CountAsync(b => b.Name == "Di trú dữ liệu 2026"));
        }

        var undo = await client.PostJsonAsync($"/api/ai-actions/{log.Id}/undo", new { });
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);

        await using var db = scenario.NewDbContext();

        // SOFT delete: row vẫn còn (KHÔNG hard delete — bất biến append-only của DB design §7)…
        var board = await db.Boards.IgnoreQueryFilters().SingleAsync(b => b.Name == "Di trú dữ liệu 2026");
        Assert.NotNull(board.DeletedAt);

        // …nhưng biến mất khỏi mọi đường đọc.
        var boards = await client.GetJsonAsync<List<BoardResponse>>($"/api/workspaces/{seed.Workspace.Id}/boards");
        Assert.DoesNotContain(boards!, b => b.Id == board.Id);
        Assert.Single(boards!);

        var dashboard = await client.GetJsonAsync<JsonElement>($"/api/workspaces/{seed.Workspace.Id}/dashboard");
        var dashboardBoards = dashboard.GetProperty("boards").EnumerateArray().ToList();

        Assert.DoesNotContain(
            dashboardBoards,
            b => b.GetProperty("boardId").GetGuid() == board.Id);

        // Ghi chú có chủ ý: `recentActivities` VẪN giữ `boardId` của bảng đã xoá. Đó là dòng lịch sử
        // (`activity_logs` append-only, không soft-delete) và ẩn nó đi sẽ là viết lại quá khứ — Phase 14
        // không đổi hành vi đó, nên test khẳng định phạm vi THẬT: danh sách bảng + số liệu.

        // Task của bảng đó cũng không còn hiện ra (query filter đi theo board đã soft-delete).
        Assert.Equal(0, await db.Tasks.CountAsync(t => t.BoardId == board.Id));

        // Log đã chuyển trạng thái, và không có bảng nào khác bị ảnh hưởng.
        Assert.Equal("Undone", (await client.GetJsonAsync<AiActionLogDetailDto>($"/api/ai-actions/{log.Id}"))!.Status);
        var allBoards = await db.Boards.IgnoreQueryFilters()
            .Select(b => new { b.Name, b.DeletedAt })
            .ToListAsync();
        Assert.True(
            allBoards.Count == 2 && allBoards.Count(b => b.DeletedAt == null) == 1,
            "Chỉ bảng của template bị soft-delete; bảng gốc vẫn sống. Thực tế: "
            + string.Join(", ", allBoards.Select(b => $"{b.Name}/{(b.DeletedAt is null ? "live" : "deleted")}")));
    }

    [Fact]
    public async Task BT_CONFIRM_5_DoubleApprovalIs409()
    {
        await using var scenario = await _database.CreateScenarioAsync(new ScriptedAiProvider { Content = ProposalJson });
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.Manager);

        var proposal = await GenerateProposalAsync(client, seed.Workspace.Id);
        var log = await (await ConfirmAsync(client, seed.Workspace.Id, proposal))
            .Content.ReadFromJsonAsync<AiActionLogDto>();

        var first = await client.PostJsonAsync($"/api/ai-actions/{log!.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostJsonAsync($"/api/ai-actions/{log.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // CAS thắng ⇒ đúng MỘT bảng, không phải hai.
        await using var db = scenario.NewDbContext();
        Assert.Equal(2, await db.Boards.CountAsync());
    }

    [Fact]
    public async Task BT_CONFIRM_6_AnActionFromAnotherWorkspaceIsInvisible()
    {
        await using var scenario = await _database.CreateScenarioAsync(new ScriptedAiProvider { Content = ProposalJson });
        var seed = await SeedAsync(scenario);
        using var manager = await scenario.AsUserAsync(seed.Manager);

        var proposal = await GenerateProposalAsync(manager, seed.Workspace.Id);
        var log = await (await ConfirmAsync(manager, seed.Workspace.Id, proposal))
            .Content.ReadFromJsonAsync<AiActionLogDto>();

        // Một workspace hoàn toàn khác, với người quản lý riêng.
        var other = await SeedAsync(scenario);
        using var otherClient = await scenario.AsUserAsync(other.Manager);

        var response = await otherClient.GetAsync($"/api/ai-actions/{log!.Id}");

        // 404 (không phải 200, không phải 403): người ngoài không được biết hành động này tồn tại.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- BT-REGRESS: the Phase 4 flow still behaves ------------------------

    [Fact]
    public async Task BT_REGRESS_1_TheOriginalSubTaskFlowStillWorksAndItsSnapshotHasNoNewKeys()
    {
        var scripted = new ScriptedAiProvider
        {
            Content = """
                {"summary":"Đề xuất cũ","tasks":[{"title":"Việc cũ","priority":"High"}]}
                """,
        };

        await using var scenario = await _database.CreateScenarioAsync(scripted);
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.Manager);

        var confirm = await client.PostJsonAsync(
            $"/api/boards/{seed.Board.Id}/smart-setup/confirm",
            new
            {
                description = "Luồng Smart Setup cũ phải không đổi sau Giai đoạn 14.",
                summary = "Đề xuất cũ",
                tasks = new[] { new { title = "Việc cũ", priority = "High", labels = Array.Empty<object>() } },
                columnId = seed.Todo.Id,
            });

        Assert.Equal(HttpStatusCode.Created, confirm.StatusCode);

        var log = await confirm.Content.ReadFromJsonAsync<AiActionLogDto>();
        var approve = await client.PostJsonAsync($"/api/ai-actions/{log!.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var detail = await client.GetJsonAsync<AiActionLogDetailDto>($"/api/ai-actions/{log.Id}");
        Assert.Equal("Approved", detail!.Status);
        Assert.Single(detail.CreatedTaskIds);

        // Snapshot cũ KHÔNG được mọc thêm khoá mới: `DefaultIgnoreCondition = WhenWritingNull` là thứ
        // giữ cho jsonb của Phase 4 giữ nguyên hình dạng (Phase 14 §6, P3).
        var snapshot = detail.AppliedSnapshot!.Value;
        Assert.False(snapshot.TryGetProperty("createdBoardId", out _));
        Assert.False(snapshot.TryGetProperty("createdColumnIds", out _));

        // Và Undo cũ vẫn chạy.
        var undo = await client.PostJsonAsync($"/api/ai-actions/{log.Id}/undo", new { });
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
    }

    // ---- helpers ----------------------------------------------------------

    private sealed record Seed(
        ApplicationUser Manager,
        Workspace Workspace,
        Board Board,
        BoardColumn Todo,
        BoardColumn Done);

    private static async Task<Seed> SeedAsync(TestScenario scenario)
    {
        var manager = await scenario.CreateUserAsync("Trưởng nhóm AI");
        var workspace = await scenario.CreateWorkspaceAsync(manager, $"Workspace template {Guid.NewGuid():N}");
        var (board, todo, done) = await scenario.CreateBoardAsync(workspace);

        return new Seed(manager, workspace, board, todo, done);
    }

    private static async Task AddMemberAsync(
        TestScenario scenario, Guid workspaceId, ApplicationUser user, WorkspaceRole role)
    {
        await using var db = scenario.NewDbContext();
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = workspaceId,
            UserId = user.Id,
            Role = role,
        });

        await db.SaveChangesAsync();
    }

    private static Task<HttpResponseMessage> GenerateAsync(
        TestHttpClient client, Guid workspaceId, string description)
        => client.PostJsonAsync(
            $"/api/workspaces/{workspaceId}/smart-setup/template",
            new { description });

    private static Task<HttpResponseMessage> ConfirmAsync(
        TestHttpClient client, Guid workspaceId, BoardTemplateProposal proposal)
        => client.PostJsonAsync(
            $"/api/workspaces/{workspaceId}/smart-setup/template/confirm",
            new { proposal });

    private static async Task<BoardTemplateProposal> GenerateProposalAsync(
        TestHttpClient client, Guid workspaceId)
    {
        var response = await GenerateAsync(client, workspaceId, "Di trú dữ liệu lên PostgreSQL 18");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var proposal = await response.Content.ReadFromJsonAsync<BoardTemplateProposal>();
        Assert.NotNull(proposal);
        return proposal!;
    }

    private static async Task<string> ErrorAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.TryGetProperty("error", out var error) ? error.GetString() ?? string.Empty : string.Empty;
    }

    private sealed record AiActionLogDto(
        Guid Id,
        string Action,
        string EntityType,
        Guid? EntityId,
        string Status);

    private sealed record AiActionLogDetailDto(
        Guid Id,
        string Action,
        string EntityType,
        Guid? EntityId,
        string Status,
        IReadOnlyList<Guid> CreatedTaskIds,
        JsonElement? AppliedSnapshot);
}
