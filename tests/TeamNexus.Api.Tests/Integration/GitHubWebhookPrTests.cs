using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.DTOs.Integrations;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Ai.Services.Integrations;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data.Entities;
using TeamNexus.Shared;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Sub-phase 15.3 verification suite: inbound GitHub webhooks, HMAC-SHA256 signature verification,
/// secret rotation, PR state machine (In Review / Done), and reversible AI action logs (Undo).
/// </summary>
public sealed class GitHubWebhookPrTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public GitHubWebhookPrTests(DatabaseFixture database)
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

    // ---- 1. Management API Tests ------------------------------------------

    [Fact]
    public async Task GetIntegration_WhenNotConfigured_ReturnsNotFound()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace GH 1");
        using var client = await s.AsUserAsync(admin);

        var response = await client.GetAsync(BaseUrl(ws.Id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpsertIntegration_FirstTime_ReturnsPlaintextSecretOnce_AndConfiguresIntegration()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace GH 2");
        using var client = await s.AsUserAsync(admin);

        var upsertResponse = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(
            IsEnabled: true,
            AutoMoveOnPr: true,
            AutoCreateTasksFromIssues: true,
            SyncIssueComments: true));

        Assert.Equal(HttpStatusCode.OK, upsertResponse.StatusCode);
        var body = await upsertResponse.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();
        Assert.NotNull(body);
        Assert.Equal(ws.Id, body!.WorkspaceId);
        Assert.True(body.IsEnabled);
        Assert.True(body.AutoMoveOnPr);
        Assert.False(string.IsNullOrWhiteSpace(body.WebhookSecretPlaintext));
        Assert.Equal(64, body.WebhookSecretPlaintext!.Length); // 32 bytes hex
        Assert.Equal(4, body.SecretHint.Length);
        Assert.EndsWith(body.SecretHint, body.WebhookSecretPlaintext, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(body.WebhookToken.ToString(), body.WebhookPayloadUrl);
    }

    [Fact]
    public async Task GetIntegration_AfterCreation_DoesNotExposePlaintextSecret_OnlySecretHint()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace GH 3");
        using var client = await s.AsUserAsync(admin);

        var createRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(IsEnabled: true));
        var created = await createRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();
        Assert.NotNull(created?.WebhookSecretPlaintext);

        var getRes = await client.GetAsync(BaseUrl(ws.Id));
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);

        var rawJson = await getRes.Content.ReadAsStringAsync();
        Assert.DoesNotContain(created!.WebhookSecretPlaintext!, rawJson);

        var fetched = await getRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();
        Assert.NotNull(fetched);
        Assert.Null(fetched!.WebhookSecretPlaintext);
        Assert.Equal(created.SecretHint, fetched.SecretHint);
    }

    [Fact]
    public async Task RotateSecret_UpdatesCiphertext_ReturnsNewSecretOnce()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace GH 4");
        using var client = await s.AsUserAsync(admin);

        var initRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(IsEnabled: true));
        var initial = await initRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        var rotateRes = await client.PostJsonAsync($"{BaseUrl(ws.Id)}/rotate-secret", new { });
        Assert.Equal(HttpStatusCode.OK, rotateRes.StatusCode);

        var rotated = await rotateRes.Content.ReadFromJsonAsync<RotateGitHubSecretResponse>();
        Assert.NotNull(rotated);
        Assert.NotEqual(initial!.WebhookSecretPlaintext, rotated!.WebhookSecretPlaintext);
        Assert.Equal(4, rotated.SecretHint.Length);
        Assert.EndsWith(rotated.SecretHint, rotated.WebhookSecretPlaintext, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteIntegration_RemovesConfiguration()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace GH 5");
        using var client = await s.AsUserAsync(admin);

        await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(IsEnabled: true));

        var deleteRes = await client.DeleteAsync(BaseUrl(ws.Id));
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        var getRes = await client.GetAsync(BaseUrl(ws.Id));
        Assert.Equal(HttpStatusCode.NotFound, getRes.StatusCode);
    }

    // ---- 2. Inbound Webhook Core & HMAC Verification ----------------------

    [Fact]
    public async Task Webhook_MissingOrBadSignature_Returns401_NoDbWrites()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace GH 6");
        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(IsEnabled: true));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        // Missing signature
        var req1 = new HttpRequestMessage(HttpMethod.Post, $"/api/webhooks/github/{integration!.WebhookToken}")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req1.Headers.Add("X-GitHub-Event", "ping");
        req1.Headers.Add("X-GitHub-Delivery", Guid.NewGuid().ToString());
        var res1 = await s.Factory.CreateClient().SendAsync(req1);
        Assert.Equal(HttpStatusCode.Unauthorized, res1.StatusCode);

        // Invalid signature
        var req2 = new HttpRequestMessage(HttpMethod.Post, $"/api/webhooks/github/{integration.WebhookToken}")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req2.Headers.Add("X-GitHub-Event", "ping");
        req2.Headers.Add("X-GitHub-Delivery", Guid.NewGuid().ToString());
        req2.Headers.Add("X-Hub-Signature-256", "sha256=0000000000000000000000000000000000000000000000000000000000000000");
        var res2 = await s.Factory.CreateClient().SendAsync(req2);
        Assert.Equal(HttpStatusCode.Unauthorized, res2.StatusCode);

        // Verify zero deliveries recorded
        await using var db = s.NewDbContext();
        var deliveriesCount = await db.GitHubWebhookDeliveries.CountAsync(d => d.WorkspaceId == ws.Id);
        Assert.Equal(0, deliveriesCount);
    }

    [Fact]
    public async Task Webhook_UnknownOrDisabledToken_Returns404()
    {
        await using var s = await _database.CreateScenarioAsync();
        var unknownToken = Guid.NewGuid();

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/webhooks/github/{unknownToken}")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        req.Headers.Add("X-GitHub-Event", "ping");
        req.Headers.Add("X-GitHub-Delivery", Guid.NewGuid().ToString());
        req.Headers.Add("X-Hub-Signature-256", "sha256=1111111111111111111111111111111111111111111111111111111111111111");

        var res = await s.Factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Webhook_PingEvent_Returns200_RecordsProcessedDelivery()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace GH 7");
        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(IsEnabled: true));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        var deliveryId = Guid.NewGuid().ToString();
        var request = CreateWebhookRequest(
            integration!.WebhookToken,
            "ping",
            deliveryId,
            integration.WebhookSecretPlaintext!,
            "{\"zen\":\"Keep it logically awesome.\"}");

        var response = await s.Factory.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var db = s.NewDbContext();
        var delivery = await db.GitHubWebhookDeliveries.FirstOrDefaultAsync(d => d.DeliveryId == deliveryId);
        Assert.NotNull(delivery);
        Assert.Equal("ping", delivery!.EventName);
        Assert.Equal("Processed", delivery.Status);
    }

    // ---- 3. PR State Machine & Kanban Automation -------------------------

    [Fact]
    public async Task Webhook_PullRequestOpened_Draft_CreatesLink_DoesNotMoveTask()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace GH 8");
        var (board, todo, done) = await s.CreateBoardAsync(ws, "Board Kanban");

        // Add InReview column
        await using (var setupDb = s.NewDbContext())
        {
            var inReview = new BoardColumn
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                Name = "In Review",
                Position = 1,
                IsInReview = true,
            };
            setupDb.BoardColumns.Add(inReview);
            var doneCol = await setupDb.BoardColumns.FirstAsync(c => c.Id == done.Id);
            doneCol.Position = 2;
            await setupDb.SaveChangesAsync();
        }

        using var client = await s.AsUserAsync(admin);

        // Configure integration
        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(IsEnabled: true, AutoMoveOnPr: true));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        // Create task (will be TN-1)
        var taskRes = await client.PostJsonAsync($"/api/boards/{board.Id}/tasks", new { columnId = todo.Id, title = "Thực hiện tính năng auth" });
        Assert.Equal(HttpStatusCode.Created, taskRes.StatusCode);
        var task = await taskRes.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(task);
        Assert.Equal(1L, task!.TaskNumber);

        // Send draft PR
        var payload = $$"""
        {
            "action": "opened",
            "repository": { "full_name": "octocat/TeamNexus" },
            "pull_request": {
                "number": 42,
                "title": "WIP: Feature Authentication TN-1",
                "html_url": "https://github.com/octocat/TeamNexus/pull/42",
                "body": "Initial draft PR",
                "draft": true,
                "merged": false,
                "head": { "ref": "feat/TN-1-auth" }
            }
        }
        """;

        var deliveryId = Guid.NewGuid().ToString();
        var req = CreateWebhookRequest(integration!.WebhookToken, "pull_request", deliveryId, integration.WebhookSecretPlaintext!, payload);
        var webhookRes = await s.Factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, webhookRes.StatusCode);

        // Assert: Task still in Todo column (not moved)
        await using var db = s.NewDbContext();
        var refreshedTask = await db.Tasks.FirstAsync(t => t.Id == task.Id);
        Assert.Equal(todo.Id, refreshedTask.ColumnId);

        // Assert: External link created with state = draft
        var link = await db.TaskExternalLinks.FirstOrDefaultAsync(l => l.TaskId == task.Id);
        Assert.NotNull(link);
        Assert.Equal("draft", link!.State);
        Assert.Equal("octocat/TeamNexus#42", link.ExternalId);
    }

    [Fact]
    public async Task Webhook_PullRequestReadyForReview_MovesTaskToInReview_CreatesApprovedAiActionLog()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace GH 9");
        var (board, todo, done) = await s.CreateBoardAsync(ws, "Board Kanban");

        Guid inReviewId;
        await using (var setupDb = s.NewDbContext())
        {
            inReviewId = Guid.NewGuid();
            var inReview = new BoardColumn
            {
                Id = inReviewId,
                BoardId = board.Id,
                Name = "In Review",
                Position = 1,
                IsInReview = true,
            };
            setupDb.BoardColumns.Add(inReview);
            var doneCol = await setupDb.BoardColumns.FirstAsync(c => c.Id == done.Id);
            doneCol.Position = 2;
            await setupDb.SaveChangesAsync();
        }

        using var client = await s.AsUserAsync(admin);

        // Configure integration
        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(IsEnabled: true, AutoMoveOnPr: true));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        // Create task
        var taskRes = await client.PostJsonAsync($"/api/boards/{board.Id}/tasks", new { columnId = todo.Id, title = "Lập trình giao diện thanh toán" });
        var task = await taskRes.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(task);

        // Send ready_for_review PR
        var payload = $$"""
        {
            "action": "ready_for_review",
            "repository": { "full_name": "octocat/TeamNexus" },
            "pull_request": {
                "number": 43,
                "title": "Payment gateway (#{{task!.TaskNumber}})",
                "html_url": "https://github.com/octocat/TeamNexus/pull/43",
                "body": "Ready for team review",
                "draft": false,
                "merged": false,
                "head": { "ref": "feature/payment-v1" }
            }
        }
        """;

        var deliveryId = Guid.NewGuid().ToString();
        var req = CreateWebhookRequest(integration!.WebhookToken, "pull_request", deliveryId, integration.WebhookSecretPlaintext!, payload);
        var webhookRes = await s.Factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, webhookRes.StatusCode);

        // Assert: Task moved to In Review column
        await using var db = s.NewDbContext();
        var refreshedTask = await db.Tasks.FirstAsync(t => t.Id == task.Id);
        Assert.Equal(inReviewId, refreshedTask.ColumnId);

        // Assert: External link created with state = open
        var link = await db.TaskExternalLinks.FirstOrDefaultAsync(l => l.TaskId == task.Id);
        Assert.NotNull(link);
        Assert.Equal("open", link!.State);

        // Assert: AiActionLog created in Approved status by GitHub Bot
        var actionLog = await db.AiActionLogs.FirstOrDefaultAsync(l => l.EntityId == board.Id && l.Action == AiActionTypes.GitHubPrMoveTask);
        Assert.NotNull(actionLog);
        Assert.Equal(AiActionStatus.Approved, actionLog!.Status);
        Assert.Equal(SystemUsers.GitHubBotUserId, actionLog.RequestedByUserId);
        Assert.Equal(SystemUsers.GitHubBotUserId, actionLog.DecidedByUserId);
    }

    [Fact]
    public async Task Webhook_PullRequestClosed_Merged_MovesTaskToDone_SetsCompletedAt()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace GH 10");
        var (board, todo, done) = await s.CreateBoardAsync(ws, "Board Kanban");

        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(IsEnabled: true, AutoMoveOnPr: true));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        var taskRes = await client.PostJsonAsync($"/api/boards/{board.Id}/tasks", new { columnId = todo.Id, title = "Task cần merge" });
        var task = await taskRes.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(task);

        // Send closed + merged PR
        var payload = $$"""
        {
            "action": "closed",
            "repository": { "full_name": "octocat/TeamNexus" },
            "pull_request": {
                "number": 44,
                "title": "Merge feature TN-{{task!.TaskNumber}}",
                "html_url": "https://github.com/octocat/TeamNexus/pull/44",
                "body": "PR approved and merged",
                "draft": false,
                "merged": true,
                "head": { "ref": "feat/TN-{{task.TaskNumber}}" }
            }
        }
        """;

        var deliveryId = Guid.NewGuid().ToString();
        var req = CreateWebhookRequest(integration!.WebhookToken, "pull_request", deliveryId, integration.WebhookSecretPlaintext!, payload);
        var webhookRes = await s.Factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, webhookRes.StatusCode);

        // Assert: Task moved to Done column and CompletedAt is populated
        await using var db = s.NewDbContext();
        var refreshedTask = await db.Tasks.FirstAsync(t => t.Id == task.Id);
        Assert.Equal(done.Id, refreshedTask.ColumnId);
        Assert.NotNull(refreshedTask.CompletedAt);

        // Assert: Link state = merged
        var link = await db.TaskExternalLinks.FirstOrDefaultAsync(l => l.TaskId == task.Id);
        Assert.NotNull(link);
        Assert.Equal("merged", link!.State);
    }

    [Fact]
    public async Task Webhook_PullRequestClosed_Merged_WhenOtherPrStillOpen_DoesNotMoveTaskToDone()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace GH 11");
        var (board, todo, done) = await s.CreateBoardAsync(ws, "Board Kanban");

        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(IsEnabled: true, AutoMoveOnPr: true));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        var taskRes = await client.PostJsonAsync($"/api/boards/{board.Id}/tasks", new { columnId = todo.Id, title = "Task có 2 PR" });
        var task = await taskRes.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(task);

        // Seed a second PR link that is currently open
        await using (var seedDb = s.NewDbContext())
        {
            seedDb.TaskExternalLinks.Add(new TaskExternalLink
            {
                WorkspaceId = ws.Id,
                TaskId = task!.Id,
                Provider = "github",
                LinkType = "pull_request",
                ExternalId = "octocat/TeamNexus#99",
                ExternalUrl = "https://github.com/octocat/TeamNexus/pull/99",
                Title = "Second companion PR",
                State = "open",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await seedDb.SaveChangesAsync();
        }

        // Send closed + merged for PR #45
        var payload = $$"""
        {
            "action": "closed",
            "repository": { "full_name": "octocat/TeamNexus" },
            "pull_request": {
                "number": 45,
                "title": "Partial PR TN-{{task!.TaskNumber}}",
                "html_url": "https://github.com/octocat/TeamNexus/pull/45",
                "draft": false,
                "merged": true,
                "head": { "ref": "feat/part-1" }
            }
        }
        """;

        var deliveryId = Guid.NewGuid().ToString();
        var req = CreateWebhookRequest(integration!.WebhookToken, "pull_request", deliveryId, integration.WebhookSecretPlaintext!, payload);
        var webhookRes = await s.Factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, webhookRes.StatusCode);

        // Assert: Task was NOT moved to Done because PR #99 is still open
        await using var db = s.NewDbContext();
        var refreshedTask = await db.Tasks.FirstAsync(t => t.Id == task.Id);
        Assert.Equal(todo.Id, refreshedTask.ColumnId);
        Assert.Null(refreshedTask.CompletedAt);
    }

    [Fact]
    public async Task Webhook_DuplicateDeliveryId_Returns200_Idempotent()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace GH 12");
        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(IsEnabled: true));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        var deliveryId = Guid.NewGuid().ToString();
        var payload = "{\"zen\":\"Practicality beats purity.\"}";

        var req1 = CreateWebhookRequest(integration!.WebhookToken, "ping", deliveryId, integration.WebhookSecretPlaintext!, payload);
        var res1 = await s.Factory.CreateClient().SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // Send exact same delivery second time
        var req2 = CreateWebhookRequest(integration.WebhookToken, "ping", deliveryId, integration.WebhookSecretPlaintext!, payload);
        var res2 = await s.Factory.CreateClient().SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        var body2 = await res2.Content.ReadFromJsonAsync<GitHubWebhookProcessResult>();
        Assert.NotNull(body2);
        Assert.True(body2!.IsDuplicate);

        // Assert: only 1 row in deliveries table
        await using var db = s.NewDbContext();
        var count = await db.GitHubWebhookDeliveries.CountAsync(d => d.DeliveryId == deliveryId);
        Assert.Equal(1, count);
    }

    // ---- 4. Accountability & Undo Integration ------------------------------

    [Fact]
    public async Task AiAction_Undo_RevertsTaskMovedByGitHubPr_BackToOriginalColumn()
    {
        await using var s = await _database.CreateScenarioAsync();
        var admin = await s.CreateUserAsync("Admin WS");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace GH 13");
        var (board, todo, done) = await s.CreateBoardAsync(ws, "Board Kanban");

        using var client = await s.AsUserAsync(admin);

        var putRes = await client.PutJsonAsync(BaseUrl(ws.Id), new UpsertWorkspaceGitHubIntegrationRequest(IsEnabled: true, AutoMoveOnPr: true));
        var integration = await putRes.Content.ReadFromJsonAsync<WorkspaceGitHubIntegrationResponse>();

        var taskRes = await client.PostJsonAsync($"/api/boards/{board.Id}/tasks", new { columnId = todo.Id, title = "Task to be moved and undone" });
        var task = await taskRes.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(task);

        // Trigger PR merge to move to Done
        var payload = $$"""
        {
            "action": "closed",
            "repository": { "full_name": "octocat/TeamNexus" },
            "pull_request": {
                "number": 50,
                "title": "Merge TN-{{task!.TaskNumber}}",
                "html_url": "https://github.com/octocat/TeamNexus/pull/50",
                "draft": false,
                "merged": true,
                "head": { "ref": "feat/TN-{{task.TaskNumber}}" }
            }
        }
        """;

        var deliveryId = Guid.NewGuid().ToString();
        var req = CreateWebhookRequest(integration!.WebhookToken, "pull_request", deliveryId, integration.WebhookSecretPlaintext!, payload);
        var webhookRes = await s.Factory.CreateClient().SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, webhookRes.StatusCode);

        // Verify task is in Done
        await using (var dbCheck = s.NewDbContext())
        {
            var inDoneTask = await dbCheck.Tasks.FirstAsync(t => t.Id == task.Id);
            Assert.Equal(done.Id, inDoneTask.ColumnId);
            Assert.NotNull(inDoneTask.CompletedAt);
        }

        // Find the AiActionLog
        Guid logId;
        await using (var dbLog = s.NewDbContext())
        {
            var log = await dbLog.AiActionLogs
                .FirstAsync(l => l.EntityId == board.Id && l.Action == AiActionTypes.GitHubPrMoveTask);
            logId = log.Id;
        }

        // Manager calls Undo
        var undoRes = await client.PostJsonAsync($"/api/ai-actions/{logId}/undo", new { });
        Assert.Equal(HttpStatusCode.OK, undoRes.StatusCode);

        var undoBody = await undoRes.Content.ReadFromJsonAsync<AiActionLogDetailResponse>();
        Assert.NotNull(undoBody);
        Assert.Equal("Undone", undoBody!.Status);

        // Verify task reverted to Todo and CompletedAt cleared
        await using (var finalDb = s.NewDbContext())
        {
            var revertedTask = await finalDb.Tasks.FirstAsync(t => t.Id == task.Id);
            Assert.Equal(todo.Id, revertedTask.ColumnId);
            Assert.Null(revertedTask.CompletedAt);
        }
    }
}
