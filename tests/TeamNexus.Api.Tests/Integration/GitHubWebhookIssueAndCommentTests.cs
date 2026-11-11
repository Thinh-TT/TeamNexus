using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.DTOs.Integrations;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Ai.Services.Integrations;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;
using TeamNexus.Shared;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Sub-phase 15.4 verification suite: GitHub Issue -> Task creation, Jev 1.13 system auto-triage,
/// label filtering, deduplication, reversible AI action log (Undo), issue link state machine,
/// and one-way comment synchronization (GitHub -> TeamNexus) via GitHub Bot.
/// </summary>
public sealed class GitHubWebhookIssueAndCommentTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public GitHubWebhookIssueAndCommentTests(DatabaseFixture database)
    {
        _database = database;
    }

    private static string BaseUrl(Guid workspaceId) => $"/api/workspaces/{workspaceId}/integrations/github";

    private static HttpRequestMessage CreateWebhookRequest(
        Guid webhookToken,
        string eventName,
        string deliveryId,
        string secret,
        string jsonPayload)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(jsonPayload);
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var hash = HMACSHA256.HashData(secretBytes, bodyBytes);
        var signature = $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/webhooks/github/{webhookToken}")
        {
            Content = new ByteArrayContent(bodyBytes)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-GitHub-Event", eventName);
        request.Headers.Add("X-GitHub-Delivery", deliveryId);
        request.Headers.Add("X-Hub-Signature-256", signature);

        return request;
    }

    // ---- 1. Issue -> Task Creation & Label Filtering Tests ------------------

    [Fact]
    public async Task IssuesOpened_WithoutMatchingLabel_WhenFilterConfigured_IgnoresAndDoesNotCreateTask()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace Issues 1");
        var (board, _, _) = await s.CreateBoardAsync(ws, "Board 1");
        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(
            IsEnabled: true,
            DefaultBoardId: board.Id,
            IssueLabelFilter: "teamnexus,bug",
            AutoCreateTasksFromIssues: true));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        var deliveryId = Guid.NewGuid().ToString();
        var payload = $$"""
        {
            "action": "opened",
            "repository": { "full_name": "octocat/Hello-World" },
            "issue": {
                "number": 10,
                "title": "Documentation update",
                "html_url": "https://github.com/octocat/Hello-World/issues/10",
                "body": "Please update docs",
                "labels": [{ "name": "documentation" }]
            },
            "sender": { "login": "octocat", "type": "User" }
        }
        """;

        var req = CreateWebhookRequest(integration!.WebhookToken, "issues", deliveryId, integration.WebhookSecretPlaintext!, payload);
        var res = await s.Factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        await using var db = s.NewDbContext();
        var delivery = await db.GitHubWebhookDeliveries.FirstOrDefaultAsync(d => d.DeliveryId == deliveryId);
        Assert.NotNull(delivery);
        Assert.Equal("Ignored", delivery!.Status);
        Assert.Contains("không chứa nhãn nào thuộc bộ lọc", delivery.Summary);

        var tasksCount = await db.Tasks.CountAsync(t => t.BoardId == board.Id);
        Assert.Equal(0, tasksCount);
    }

    [Fact]
    public async Task IssuesOpened_WithMatchingLabel_CreatesTask_WithExternalLink_AndTaskNumber()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace Issues 2");
        var (board, todo, _) = await s.CreateBoardAsync(ws, "Board 2");
        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(
            IsEnabled: true,
            DefaultBoardId: board.Id,
            IssueLabelFilter: "teamnexus,bug",
            AutoCreateTasksFromIssues: true));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        var deliveryId = Guid.NewGuid().ToString();
        var payload = $$"""
        {
            "action": "opened",
            "repository": { "full_name": "octocat/Hello-World" },
            "issue": {
                "number": 42,
                "title": "Sửa lỗi crash khi khởi động",
                "html_url": "https://github.com/octocat/Hello-World/issues/42",
                "body": "Ứng dụng bị sập khi khởi động lần đầu.",
                "labels": [{ "name": "bug" }, { "name": "urgent" }]
            },
            "sender": { "login": "octocat", "type": "User" }
        }
        """;

        var req = CreateWebhookRequest(integration!.WebhookToken, "issues", deliveryId, integration.WebhookSecretPlaintext!, payload);
        var res = await s.Factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        await using var db = s.NewDbContext();
        var delivery = await db.GitHubWebhookDeliveries.FirstOrDefaultAsync(d => d.DeliveryId == deliveryId);
        Assert.NotNull(delivery);
        Assert.Equal("Processed", delivery!.Status);

        var task = await db.Tasks.FirstOrDefaultAsync(t => t.BoardId == board.Id);
        Assert.NotNull(task);
        Assert.Equal("[GH #42] Sửa lỗi crash khi khởi động", task!.Title);
        Assert.Equal(SystemUsers.GitHubBotUserId, task.CreatedBy);
        Assert.True(task.TaskNumber > 0);
        Assert.Contains("GitHub Issue #42", task.Description);
        Assert.Contains("octocat", task.Description);

        var link = await db.TaskExternalLinks.FirstOrDefaultAsync(l => l.TaskId == task.Id);
        Assert.NotNull(link);
        Assert.Equal("github", link!.Provider);
        Assert.Equal("issue", link.LinkType);
        Assert.Equal("octocat/Hello-World#42", link.ExternalId);
        Assert.Equal("open", link.State);
    }

    [Fact]
    public async Task IssuesLabeled_WhenMatchingLabelAddedLater_CreatesTask()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace Issues 3");
        var (board, _, _) = await s.CreateBoardAsync(ws, "Board 3");
        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(
            IsEnabled: true,
            DefaultBoardId: board.Id,
            IssueLabelFilter: "teamnexus",
            AutoCreateTasksFromIssues: true));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        var deliveryId = Guid.NewGuid().ToString();
        var payload = $$"""
        {
            "action": "labeled",
            "repository": { "full_name": "octocat/Hello-World" },
            "issue": {
                "number": 55,
                "title": "Cần thêm tính năng export excel",
                "html_url": "https://github.com/octocat/Hello-World/issues/55",
                "body": "Khách hàng yêu cầu xuất Excel báo cáo tuần.",
                "labels": [{ "name": "teamnexus" }]
            },
            "sender": { "login": "monalisa", "type": "User" }
        }
        """;

        var req = CreateWebhookRequest(integration!.WebhookToken, "issues", deliveryId, integration.WebhookSecretPlaintext!, payload);
        var res = await s.Factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        await using var db = s.NewDbContext();
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.BoardId == board.Id);
        Assert.NotNull(task);
        Assert.Equal("[GH #55] Cần thêm tính năng export excel", task!.Title);
    }

    [Fact]
    public async Task IssuesLabeled_WhenAlreadyLinked_DoesNotCreateDuplicateTask()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace Issues 4");
        var (board, _, _) = await s.CreateBoardAsync(ws, "Board 4");
        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(
            IsEnabled: true,
            DefaultBoardId: board.Id,
            IssueLabelFilter: "teamnexus,bug",
            AutoCreateTasksFromIssues: true));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        // 1st event: opened with teamnexus label
        var payload1 = $$"""
        {
            "action": "opened",
            "repository": { "full_name": "octocat/Hello-World" },
            "issue": {
                "number": 66,
                "title": "Bug thanh toán VNPay",
                "html_url": "https://github.com/octocat/Hello-World/issues/66",
                "body": "Lỗi chữ ký checksum",
                "labels": [{ "name": "teamnexus" }]
            },
            "sender": { "login": "dev1", "type": "User" }
        }
        """;
        var req1 = CreateWebhookRequest(integration!.WebhookToken, "issues", Guid.NewGuid().ToString(), integration.WebhookSecretPlaintext!, payload1);
        var res1 = await s.Factory.CreateClient().SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // 2nd event: labeled with bug on the SAME issue
        var deliveryId2 = Guid.NewGuid().ToString();
        var payload2 = $$"""
        {
            "action": "labeled",
            "repository": { "full_name": "octocat/Hello-World" },
            "issue": {
                "number": 66,
                "title": "Bug thanh toán VNPay",
                "html_url": "https://github.com/octocat/Hello-World/issues/66",
                "body": "Lỗi chữ ký checksum",
                "labels": [{ "name": "teamnexus" }, { "name": "bug" }]
            },
            "sender": { "login": "lead", "type": "User" }
        }
        """;
        var req2 = CreateWebhookRequest(integration!.WebhookToken, "issues", deliveryId2, integration.WebhookSecretPlaintext!, payload2);
        var res2 = await s.Factory.CreateClient().SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        await using var db = s.NewDbContext();
        var tasksCount = await db.Tasks.CountAsync(t => t.BoardId == board.Id);
        Assert.Equal(1, tasksCount); // Exactly 1 task, no duplicate!

        var delivery2 = await db.GitHubWebhookDeliveries.FirstOrDefaultAsync(d => d.DeliveryId == deliveryId2);
        Assert.NotNull(delivery2);
        Assert.Equal("Ignored", delivery2!.Status);
        Assert.Contains("đã được liên kết với thẻ", delivery2.Summary);
    }

    [Fact]
    public async Task IssuesOpened_WithNoLabelFilter_CreatesTaskDirectly()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace Issues 5");
        var (board, _, _) = await s.CreateBoardAsync(ws, "Board 5");
        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(
            IsEnabled: true,
            DefaultBoardId: board.Id,
            IssueLabelFilter: null, // No filter!
            AutoCreateTasksFromIssues: true));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        var deliveryId = Guid.NewGuid().ToString();
        var payload = $$"""
        {
            "action": "opened",
            "repository": { "full_name": "octocat/Hello-World" },
            "issue": {
                "number": 77,
                "title": "Tác vụ không có nhãn",
                "html_url": "https://github.com/octocat/Hello-World/issues/77",
                "body": "Nội dung bất kỳ",
                "labels": []
            },
            "sender": { "login": "octocat", "type": "User" }
        }
        """;

        var req = CreateWebhookRequest(integration!.WebhookToken, "issues", deliveryId, integration.WebhookSecretPlaintext!, payload);
        var res = await s.Factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        await using var db = s.NewDbContext();
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.BoardId == board.Id);
        Assert.NotNull(task);
        Assert.Equal("[GH #77] Tác vụ không có nhãn", task!.Title);
    }

    [Fact]
    public async Task IssuesOpened_WhenAutoCreateTasksFromIssuesIsFalse_Ignores()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace Issues 6");
        var (board, _, _) = await s.CreateBoardAsync(ws, "Board 6");
        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(
            IsEnabled: true,
            DefaultBoardId: board.Id,
            AutoCreateTasksFromIssues: false));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        var deliveryId = Guid.NewGuid().ToString();
        var payload = $$"""
        {
            "action": "opened",
            "repository": { "full_name": "octocat/Hello-World" },
            "issue": {
                "number": 88,
                "title": "Issue when disabled",
                "html_url": "https://github.com/octocat/Hello-World/issues/88"
            },
            "sender": { "login": "octocat", "type": "User" }
        }
        """;

        var req = CreateWebhookRequest(integration!.WebhookToken, "issues", deliveryId, integration.WebhookSecretPlaintext!, payload);
        var res = await s.Factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        await using var db = s.NewDbContext();
        var delivery = await db.GitHubWebhookDeliveries.FirstOrDefaultAsync(d => d.DeliveryId == deliveryId);
        Assert.NotNull(delivery);
        Assert.Equal("Ignored", delivery!.Status);
        Assert.Contains("Tính năng tự động tạo thẻ từ Issue đang bị tắt", delivery.Summary);
    }

    // ---- 2. AI Action Log & Undo Tests ------------------------------------

    [Fact]
    public async Task IssuesOpened_CreatesApprovedAiActionLog_AndUndo_SoftDeletesCreatedTask()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace Issues 7");
        var (board, _, _) = await s.CreateBoardAsync(ws, "Board 7");
        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(
            IsEnabled: true,
            DefaultBoardId: board.Id,
            AutoCreateTasksFromIssues: true));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        var deliveryId = Guid.NewGuid().ToString();
        var payload = $$"""
        {
            "action": "opened",
            "repository": { "full_name": "octocat/Hello-World" },
            "issue": {
                "number": 99,
                "title": "Task có thể hoàn tác",
                "html_url": "https://github.com/octocat/Hello-World/issues/99",
                "body": "Kiểm tra cơ chế Undo qua AiActionService"
            },
            "sender": { "login": "tester", "type": "User" }
        }
        """;

        var req = CreateWebhookRequest(integration!.WebhookToken, "issues", deliveryId, integration.WebhookSecretPlaintext!, payload);
        var res = await s.Factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        await using var db = s.NewDbContext();
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.BoardId == board.Id);
        Assert.NotNull(task);

        var actionLog = await db.AiActionLogs
            .FirstOrDefaultAsync(l => l.Action == AiActionTypes.GitHubIssueCreateTask && l.EntityId == board.Id);
        Assert.NotNull(actionLog);
        Assert.Equal(AiActionStatus.Approved, actionLog!.Status);
        Assert.Equal(SystemUsers.GitHubBotUserId, actionLog.RequestedByUserId);
        Assert.Equal(SystemUsers.GitHubBotUserId, actionLog.DecidedByUserId);
        Assert.Contains(task!.Id.ToString(), actionLog.AppliedSnapshot);

        // Perform UNDO via API
        var undoRes = await client.PostJsonAsync($"/api/ai-actions/{actionLog.Id}/undo", new { });
        Assert.Equal(HttpStatusCode.OK, undoRes.StatusCode);
        var undoDetail = await undoRes.Content.ReadFromJsonAsync<AiActionLogDetailResponse>();
        Assert.NotNull(undoDetail);
        Assert.Equal("Undone", undoDetail!.Status);

        // Verify task is soft-deleted.
        //
        // The Undo ran in the API host's own DbContext, so `db` (opened earlier in this test) still tracks
        // the pre-undo instance. EF Core's identity map returns that stale instance for a tracked query —
        // `IgnoreQueryFilters()` would then report `DeletedAt == null` even though the row IS soft-deleted.
        // A fresh context is what makes this assertion observe the database rather than the change tracker.
        await using var verifyDb = s.NewDbContext();
        var taskAfterUndo = await verifyDb.Tasks.FirstOrDefaultAsync(t => t.Id == task.Id);
        Assert.Null(taskAfterUndo); // Default query filter hides deleted tasks

        var softDeleted = await verifyDb.Tasks.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == task.Id);
        Assert.NotNull(softDeleted);
        Assert.NotNull(softDeleted!.DeletedAt);
    }

    [Fact]
    public async Task IssuesOpened_WhenTriageThrows_StillCreatesTaskAndAuditLog()
    {
        // Review F7: the accountability row is written before the optional AI enrichment, so even a triage
        // explosion leaves a traced, undoable task instead of a card that redelivery can never repair.
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace Issues F7");
        var (board, _, _) = await s.CreateBoardAsync(ws, "Board F7");
        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(
            IsEnabled: true,
            DefaultBoardId: board.Id,
            AutoCreateTasksFromIssues: true));
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        await using var db = s.NewDbContext();
        var integration = await db.WorkspaceGitHubIntegrations.FirstAsync(i => i.WorkspaceId == ws.Id);

        // ITaskAutomation is scoped: resolve it from a scope, exactly like a request would.
        using var scope = s.Services.CreateScope();
        var automation = scope.ServiceProvider.GetRequiredService<ITaskAutomation>();
        var service = new GitHubWebhookService(
            db,
            automation,
            new ThrowingTriageService(),
            new TaskKeyParser(),
            NullLogger<GitHubWebhookService>.Instance);

        var deliveryId = Guid.NewGuid().ToString();
        var payload = $$"""
        {
            "action": "opened",
            "repository": { "full_name": "octocat/Hello-World" },
            "issue": {
                "number": 301,
                "title": "Issue làm Jev nổ",
                "html_url": "https://github.com/octocat/Hello-World/issues/301",
                "body": "Nội dung bất kỳ"
            },
            "sender": { "login": "octocat", "type": "User" }
        }
        """;

        var result = await service.ProcessWebhookAsync(
            integration,
            "issues",
            deliveryId,
            Encoding.UTF8.GetBytes(payload));

        Assert.Equal("Processed", result.Status);

        var task = Assert.Single(await db.Tasks.Where(t => t.BoardId == board.Id).ToListAsync());
        Assert.Equal(SystemUsers.GitHubBotUserId, task.CreatedBy);

        var log = Assert.Single(
            await db.AiActionLogs
                .Where(l => l.Action == AiActionTypes.GitHubIssueCreateTask && l.EntityId == board.Id)
                .ToListAsync());
        Assert.Equal(AiActionStatus.Approved, log.Status);
        Assert.Contains(task.Id.ToString(), log.AppliedSnapshot);

        Assert.Single(await db.GitHubWebhookDeliveries.Where(d => d.DeliveryId == deliveryId).ToListAsync());
    }

    /// <summary>Throws on the system triage call to prove the webhook survives an AI failure (review F7).</summary>
    private sealed class ThrowingTriageService : ITaskTriageService
    {
        public Task<TaskTriageResponse> TriageAsync(
            Guid boardId, TaskTriageRequest request, Guid userId, CancellationToken ct = default)
            => throw new InvalidOperationException("The webhook path must not use interactive triage.");

        public Task<AssigneeSuggestionResponse> SuggestAssigneesAsync(
            Guid boardId, Guid taskId, Guid userId, CancellationToken ct = default)
            => throw new InvalidOperationException("The webhook path must not suggest assignees.");

        public Task<TaskTriageResponse?> TriageForSystemAsync(
            Guid boardId, string title, string? description, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
    }

    // ---- 3. Issue Link State Machine Tests (Closed / Reopened) -------------

    [Fact]
    public async Task IssuesClosed_And_Reopened_UpdatesLinkStateAccurately_AndBroadcastsTaskUpdated()
    {
        var boardEvents = new RecordingBoardEventPublisher();
        await using var s = await _database.CreateScenarioAsync(boardEvents: boardEvents);
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace Issues 8");
        var (board, _, _) = await s.CreateBoardAsync(ws, "Board 8");
        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(
            IsEnabled: true,
            DefaultBoardId: board.Id,
            AutoCreateTasksFromIssues: true));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        // 1. Open issue
        var openPayload = $$"""
        {
            "action": "opened",
            "repository": { "full_name": "octocat/Hello-World" },
            "issue": {
                "number": 123,
                "title": "Issue theo dõi vòng đời",
                "html_url": "https://github.com/octocat/Hello-World/issues/123"
            },
            "sender": { "login": "octocat", "type": "User" }
        }
        """;
        var openReq = CreateWebhookRequest(integration!.WebhookToken, "issues", Guid.NewGuid().ToString(), integration.WebhookSecretPlaintext!, openPayload);
        await s.Factory.CreateClient().SendAsync(openReq);

        Guid taskId;
        await using (var db = s.NewDbContext())
        {
            var link = await db.TaskExternalLinks.FirstOrDefaultAsync(l => l.ExternalId == "octocat/Hello-World#123");
            Assert.NotNull(link);
            Assert.Equal("open", link!.State);
            taskId = link.TaskId;
        }

        // Jev already broadcast one TaskUpdated while applying its priority/tag suggestions, so the test
        // counts deltas instead of assuming the webhook is the only publisher.
        var updatesBeforeClose = boardEvents.TaskUpdatedEvents.Count(e => e.Task.Id == taskId);

        // 2. Close issue
        var closePayload = $$"""
        {
            "action": "closed",
            "repository": { "full_name": "octocat/Hello-World" },
            "issue": {
                "number": 123,
                "title": "Issue theo dõi vòng đời",
                "html_url": "https://github.com/octocat/Hello-World/issues/123"
            },
            "sender": { "login": "octocat", "type": "User" }
        }
        """;
        var closeReq = CreateWebhookRequest(integration.WebhookToken, "issues", Guid.NewGuid().ToString(), integration.WebhookSecretPlaintext!, closePayload);
        var closeRes = await s.Factory.CreateClient().SendAsync(closeReq);
        Assert.Equal(HttpStatusCode.OK, closeRes.StatusCode);

        await using (var db = s.NewDbContext())
        {
            var link = await db.TaskExternalLinks.FirstOrDefaultAsync(l => l.ExternalId == "octocat/Hello-World#123");
            Assert.NotNull(link);
            Assert.Equal("closed", link!.State);
        }

        // Review F6: closing an issue must broadcast TaskUpdated with the new chip state, otherwise the
        // board keeps showing "open" until the user reloads.
        Assert.Equal(updatesBeforeClose + 1, boardEvents.TaskUpdatedEvents.Count(e => e.Task.Id == taskId));
        var closedUpdate = boardEvents.TaskUpdatedEvents.Last(e => e.Task.Id == taskId);
        Assert.Equal(board.Id, closedUpdate.BoardId);
        Assert.Equal(
            "closed",
            Assert.Single(
                closedUpdate.Task.ExternalLinks!,
                l => l.ExternalId == "octocat/Hello-World#123").State);

        // 3. Reopen issue
        var reopenPayload = $$"""
        {
            "action": "reopened",
            "repository": { "full_name": "octocat/Hello-World" },
            "issue": {
                "number": 123,
                "title": "Issue theo dõi vòng đời",
                "html_url": "https://github.com/octocat/Hello-World/issues/123"
            },
            "sender": { "login": "octocat", "type": "User" }
        }
        """;
        var reopenReq = CreateWebhookRequest(integration.WebhookToken, "issues", Guid.NewGuid().ToString(), integration.WebhookSecretPlaintext!, reopenPayload);
        var reopenRes = await s.Factory.CreateClient().SendAsync(reopenReq);
        Assert.Equal(HttpStatusCode.OK, reopenRes.StatusCode);

        await using (var db = s.NewDbContext())
        {
            var link = await db.TaskExternalLinks.FirstOrDefaultAsync(l => l.ExternalId == "octocat/Hello-World#123");
            Assert.NotNull(link);
            Assert.Equal("open", link!.State);
        }

        // Reopening flips the chip back and broadcasts again — the same contract in the other direction.
        var reopenUpdate = boardEvents.TaskUpdatedEvents.Last(e => e.Task.Id == taskId);
        Assert.Equal(
            "open",
            Assert.Single(
                reopenUpdate.Task.ExternalLinks!,
                l => l.ExternalId == "octocat/Hello-World#123").State);
        Assert.Equal(updatesBeforeClose + 2, boardEvents.TaskUpdatedEvents.Count(e => e.Task.Id == taskId));
    }

    // ---- 4. Comment Synchronization Tests (issue_comment.created) ----------

    [Fact]
    public async Task IssueCommentCreated_OnLinkedIssue_AddsCommentFromGitHubBot()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace Comments 1");
        var (board, _, _) = await s.CreateBoardAsync(ws, "Board C1");
        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(
            IsEnabled: true,
            DefaultBoardId: board.Id,
            AutoCreateTasksFromIssues: true,
            SyncIssueComments: true));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        // Create task from issue
        var issuePayload = $$"""
        {
            "action": "opened",
            "repository": { "full_name": "octocat/Hello-World" },
            "issue": {
                "number": 200,
                "title": "Issue nhận comment",
                "html_url": "https://github.com/octocat/Hello-World/issues/200"
            },
            "sender": { "login": "dev1", "type": "User" }
        }
        """;
        await s.Factory.CreateClient().SendAsync(CreateWebhookRequest(integration!.WebhookToken, "issues", Guid.NewGuid().ToString(), integration.WebhookSecretPlaintext!, issuePayload));

        // Post comment on that issue
        var deliveryId = Guid.NewGuid().ToString();
        var commentPayload = $$"""
        {
            "action": "created",
            "repository": { "full_name": "octocat/Hello-World" },
            "issue": {
                "number": 200,
                "html_url": "https://github.com/octocat/Hello-World/issues/200"
            },
            "comment": {
                "id": 999,
                "body": "Đã reproduce được lỗi trên môi trường staging.",
                "html_url": "https://github.com/octocat/Hello-World/issues/200#issuecomment-999"
            },
            "sender": { "login": "qa_tester", "type": "User" }
        }
        """;

        var req = CreateWebhookRequest(integration.WebhookToken, "issue_comment", deliveryId, integration.WebhookSecretPlaintext!, commentPayload);
        var res = await s.Factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        await using var db = s.NewDbContext();
        var delivery = await db.GitHubWebhookDeliveries.FirstOrDefaultAsync(d => d.DeliveryId == deliveryId);
        Assert.NotNull(delivery);
        Assert.Equal("Processed", delivery!.Status);

        var task = await db.Tasks.FirstOrDefaultAsync(t => t.BoardId == board.Id);
        Assert.NotNull(task);

        var comment = await db.TaskComments.FirstOrDefaultAsync(c => c.TaskId == task!.Id);
        Assert.NotNull(comment);
        Assert.Equal(SystemUsers.GitHubBotUserId, comment!.AuthorId);
        Assert.Contains("@qa_tester", comment.Content);
        Assert.Contains("Đã reproduce được lỗi", comment.Content);
        Assert.Contains("https://github.com/octocat/Hello-World/issues/200#issuecomment-999", comment.Content);
    }

    [Fact]
    public async Task IssueCommentCreated_WithTaskNumberInBody_AddsCommentToReferencedTask()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace Comments 2");
        var (board, todo, _) = await s.CreateBoardAsync(ws, "Board C2");
        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(
            IsEnabled: true,
            DefaultBoardId: board.Id,
            SyncIssueComments: true));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        // Create an independent task (will have TN-1)
        var taskRes = await client.PostJsonAsync($"/api/boards/{board.Id}/tasks", new { columnId = todo.Id, title = "Thẻ nhận diện qua body" });
        Assert.Equal(HttpStatusCode.Created, taskRes.StatusCode);
        var createdTask = await taskRes.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(createdTask);
        Assert.Equal(1L, createdTask!.TaskNumber);

        // Send comment mentioning TN-1 on unlinked issue
        var deliveryId = Guid.NewGuid().ToString();
        var commentPayload = $$"""
        {
            "action": "created",
            "repository": { "full_name": "octocat/Hello-World" },
            "issue": {
                "number": 999,
                "html_url": "https://github.com/octocat/Hello-World/issues/999"
            },
            "comment": {
                "id": 1001,
                "body": "Vui lòng kiểm tra lại liên quan đến TN-1 trước khi deploy.",
                "html_url": "https://github.com/octocat/Hello-World/issues/999#issuecomment-1001"
            },
            "sender": { "login": "techlead", "type": "User" }
        }
        """;

        var req = CreateWebhookRequest(integration!.WebhookToken, "issue_comment", deliveryId, integration.WebhookSecretPlaintext!, commentPayload);
        var res = await s.Factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        await using var db = s.NewDbContext();
        var comment = await db.TaskComments.FirstOrDefaultAsync(c => c.TaskId == createdTask.Id);
        Assert.NotNull(comment);
        Assert.Equal(SystemUsers.GitHubBotUserId, comment!.AuthorId);
        Assert.Contains("@techlead", comment.Content);
        Assert.Contains("TN-1", comment.Content);
    }

    [Fact]
    public async Task IssueCommentCreated_WhenSenderIsBot_IsIgnored()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace Comments 3");
        var (board, _, _) = await s.CreateBoardAsync(ws, "Board C3");
        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(
            IsEnabled: true,
            DefaultBoardId: board.Id,
            SyncIssueComments: true));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        var deliveryId = Guid.NewGuid().ToString();
        var commentPayload = $$"""
        {
            "action": "created",
            "repository": { "full_name": "octocat/Hello-World" },
            "issue": { "number": 100 },
            "comment": {
                "id": 555,
                "body": "Automated code analysis passed.",
                "html_url": "https://github.com/octocat/Hello-World/issues/100#issuecomment-555"
            },
            "sender": { "login": "codecov[bot]", "type": "Bot" }
        }
        """;

        var req = CreateWebhookRequest(integration!.WebhookToken, "issue_comment", deliveryId, integration.WebhookSecretPlaintext!, commentPayload);
        var res = await s.Factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        await using var db = s.NewDbContext();
        var delivery = await db.GitHubWebhookDeliveries.FirstOrDefaultAsync(d => d.DeliveryId == deliveryId);
        Assert.NotNull(delivery);
        Assert.Equal("Ignored", delivery!.Status);
        Assert.Contains("Bỏ qua bình luận từ Bot", delivery.Summary);

        var commentCount = await db.TaskComments.CountAsync();
        Assert.Equal(0, commentCount);
    }

    [Fact]
    public async Task IssueCommentCreated_WhenSyncDisabled_IsIgnored()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace Comments 4");
        var (board, _, _) = await s.CreateBoardAsync(ws, "Board C4");
        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(
            IsEnabled: true,
            DefaultBoardId: board.Id,
            SyncIssueComments: false));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        var deliveryId = Guid.NewGuid().ToString();
        var commentPayload = $$"""
        {
            "action": "created",
            "repository": { "full_name": "octocat/Hello-World" },
            "issue": { "number": 100 },
            "comment": {
                "id": 777,
                "body": "User comment when sync disabled",
                "html_url": "https://github.com/octocat/Hello-World/issues/100#issuecomment-777"
            },
            "sender": { "login": "user1", "type": "User" }
        }
        """;

        var req = CreateWebhookRequest(integration!.WebhookToken, "issue_comment", deliveryId, integration.WebhookSecretPlaintext!, commentPayload);
        var res = await s.Factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        await using var db = s.NewDbContext();
        var delivery = await db.GitHubWebhookDeliveries.FirstOrDefaultAsync(d => d.DeliveryId == deliveryId);
        Assert.NotNull(delivery);
        Assert.Equal("Ignored", delivery!.Status);
        Assert.Contains("Tính năng đồng bộ bình luận GitHub đang bị tắt", delivery.Summary);
    }
}
