using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Ai.Services.Integrations;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Sub-phase 15.2 — outbound Slack/Discord webhooks: management API, the notification → outbox hook,
/// the dispatcher's retry/circuit-breaker rules. HTTP to Slack/Discord is always a stub.
/// </summary>
public sealed class WorkspaceWebhooksApiTests : IClassFixture<DatabaseFixture>
{
    private const string SlackUrl = "https://hooks.slack.com/services/T0001/B0002/secretsecret1234";
    private const string DiscordUrl = "https://discord.com/api/webhooks/123456/discordsecret9876";

    private readonly DatabaseFixture _database;

    public WorkspaceWebhooksApiTests(DatabaseFixture database) => _database = database;

    private static string Base(Guid ws) => $"/api/workspaces/{ws}/integrations/webhooks";

    private static async Task<(ApplicationUser Admin, ApplicationUser Member, Workspace Workspace)> SeedAsync(TestScenario s)
    {
        var admin = await s.CreateUserAsync("Quản lý", "wh.admin@example.test");
        var member = await s.CreateUserAsync("Thành viên", "wh.member@example.test");
        var ws = await s.CreateWorkspaceAsync(admin, "Workspace Webhook", (member, WorkspaceRole.Member));
        return (admin, member, ws);
    }

    private static async Task<Guid> CreateEndpointAsync(
        TestHttpClient client, Guid ws, string provider, string url, string? events = null)
    {
        var response = await client.PostJsonAsync(Base(ws), new
        {
            name = $"Kênh {provider}",
            provider,
            targetUrl = url,
            subscribedEvents = events,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    // ---- management API ----------------------------------------------------

    [Fact]
    public async Task Crud_MasksTheUrl_AndAcceptsArrayOrCsvEvents()
    {
        await using var s = await _database.CreateScenarioAsync();
        var (admin, _, ws) = await SeedAsync(s);
        using var client = await s.AsUserAsync(admin);

        var slackId = await CreateEndpointAsync(client, ws.Id, "slack", SlackUrl, "ObserverAlert,MemberJoined");

        var arrayResponse = await client.PostJsonAsync(Base(ws.Id), new
        {
            name = "Discord",
            provider = "discord",
            targetUrl = DiscordUrl,
            subscribedEvents = new[] { "TaskAssigned" },
        });
        Assert.Equal(HttpStatusCode.Created, arrayResponse.StatusCode);

        var raw = await (await client.GetAsync(Base(ws.Id))).Content.ReadAsStringAsync();
        Assert.DoesNotContain("secretsecret", raw);
        Assert.DoesNotContain("discordsecret", raw);
        Assert.Contains("****1234", raw);

        var patch = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"{Base(ws.Id)}/{slackId}")
        {
            Content = JsonContent.Create(new { name = "Đổi tên", subscribedEvents = "TaskAssigned" }),
        });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var patched = await patch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Đổi tên", patched.GetProperty("name").GetString());
        Assert.Equal("TaskAssigned", patched.GetProperty("subscribedEventsCsv").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Base(ws.Id)}/{slackId}")).StatusCode);
        Assert.Equal(1, await s.CountAsync<WorkspaceWebhookEndpoint>());
    }

    [Theory]
    [InlineData("slack", "http://hooks.slack.com/services/T/B/x")]
    [InlineData("slack", "https://127.0.0.1/services/T/B/x")]
    [InlineData("discord", "https://169.254.169.254/api/webhooks/1/x")]
    [InlineData("teams", "https://hooks.slack.com/services/T/B/x")]
    public async Task Create_WithAnUnsafeTarget_IsRejected(string provider, string url)
    {
        await using var s = await _database.CreateScenarioAsync();
        var (admin, _, ws) = await SeedAsync(s);
        using var client = await s.AsUserAsync(admin);

        var response = await client.PostJsonAsync(Base(ws.Id), new { name = "x", provider, targetUrl = url });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await s.CountAsync<WorkspaceWebhookEndpoint>());
    }

    [Fact]
    public async Task Create_RejectsAnUnknownEventKey()
    {
        await using var s = await _database.CreateScenarioAsync();
        var (admin, _, ws) = await SeedAsync(s);
        using var client = await s.AsUserAsync(admin);

        var response = await client.PostJsonAsync(
            Base(ws.Id), new { name = "x", provider = "slack", targetUrl = SlackUrl, subscribedEvents = "Nonsense" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_IsCappedAtTenEndpointsPerWorkspace()
    {
        await using var s = await _database.CreateScenarioAsync();
        var (admin, _, ws) = await SeedAsync(s);
        using var client = await s.AsUserAsync(admin);

        for (var i = 0; i < 10; i++)
        {
            await CreateEndpointAsync(client, ws.Id, "slack", SlackUrl);
        }

        var eleventh = await client.PostJsonAsync(
            Base(ws.Id), new { name = "x", provider = "slack", targetUrl = SlackUrl });
        Assert.Equal(HttpStatusCode.BadRequest, eleventh.StatusCode);
    }

    [Fact]
    public async Task Access_MemberIsForbidden_OutsiderIsNotFound_MissingCsrfIsForbidden()
    {
        await using var s = await _database.CreateScenarioAsync();
        var (admin, member, ws) = await SeedAsync(s);
        var outsider = await s.CreateUserAsync("Người ngoài", "wh.out@example.test");

        using (var memberClient = await s.AsUserAsync(member))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.GetAsync(Base(ws.Id))).StatusCode);
        }

        using (var outsiderClient = await s.AsUserAsync(outsider))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await outsiderClient.GetAsync(Base(ws.Id))).StatusCode);
        }

        using var adminClient = await s.AsUserAsync(admin);
        var noCsrf = await adminClient.Http.SendAsync(new HttpRequestMessage(HttpMethod.Post, Base(ws.Id))
        {
            Content = JsonContent.Create(new { name = "x", provider = "slack", targetUrl = SlackUrl }),
        });
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
    }

    // ---- notification → outbox ---------------------------------------------

    [Fact]
    public async Task AssigningATask_EnqueuesOnlyForEndpointsSubscribedToIt()
    {
        await using var s = await _database.CreateScenarioAsync();
        var (admin, member, ws) = await SeedAsync(s);
        var (board, todo, _) = await s.CreateBoardAsync(ws);
        using var client = await s.AsUserAsync(admin);

        var subscribed = await CreateEndpointAsync(client, ws.Id, "slack", SlackUrl, "TaskAssigned");
        await CreateEndpointAsync(client, ws.Id, "discord", DiscordUrl, "MemberJoined");

        var create = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/tasks", new { columnId = todo.Id, title = "Giao việc", assigneeId = member.Id });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var rows = await s.NewDbContext().WebhookDeliveryOutbox.ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(subscribed, row.EndpointId);
        Assert.Equal("Pending", row.Status);
        Assert.Equal("TaskAssigned", row.EventType);
        Assert.Contains("blocks", row.PayloadJson);
    }

    [Fact]
    public async Task WorkspaceWithoutEndpoints_WritesNothingToTheOutbox()
    {
        await using var s = await _database.CreateScenarioAsync();
        var (admin, member, ws) = await SeedAsync(s);
        var (board, todo, _) = await s.CreateBoardAsync(ws);
        using var client = await s.AsUserAsync(admin);

        await client.PostJsonAsync(
            $"/api/boards/{board.Id}/tasks", new { columnId = todo.Id, title = "Giao việc", assigneeId = member.Id });

        Assert.Equal(1, await s.CountAsync<Notification>(n => n.Type == "TaskAssigned"));
        Assert.Equal(0, await s.CountAsync<WebhookDeliveryOutbox>());
    }

    [Fact]
    public async Task SameDedupeKey_IsEnqueuedOncePerEndpoint()
    {
        await using var s = await _database.CreateScenarioAsync();
        var (admin, _, ws) = await SeedAsync(s);
        using var client = await s.AsUserAsync(admin);
        await CreateEndpointAsync(client, ws.Id, "slack", SlackUrl, "TaskAssigned");

        var sink = s.Services.GetRequiredService<IExternalAlertSink>();
        var message = new ExternalAlertMessage(
            ws.Id, "TaskAssigned", "t", "m", null, [admin.Id], Guid.NewGuid(), "TaskAssigned:dup");
        await sink.PublishAsync(message);
        await sink.PublishAsync(message);

        Assert.Equal(1, await s.CountAsync<WebhookDeliveryOutbox>());
    }

    [Fact]
    public async Task AcceptingAnInvitation_NotifiesManagersAndEnqueuesMemberJoined()
    {
        await using var s = await _database.CreateScenarioAsync();
        var (admin, _, ws) = await SeedAsync(s);
        using var adminClient = await s.AsUserAsync(admin);
        await CreateEndpointAsync(adminClient, ws.Id, "discord", DiscordUrl, "MemberJoined");

        const string raw = "member-joined-token";
        await using (var db = s.NewDbContext())
        {
            db.WorkspaceInvitations.Add(new WorkspaceInvitation
            {
                WorkspaceId = ws.Id,
                InvitedEmail = "newbie@example.test",
                InvitedRole = WorkspaceRole.Member,
                TokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(raw))),
                InvitedByUserId = admin.Id,
                Status = InvitationStatus.Pending,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            });
            await db.SaveChangesAsync();
        }

        var newbie = await s.CreateUserAsync("Người mới", "newbie@example.test");
        using var newbieClient = await s.AsUserAsync(newbie);
        var accept = await newbieClient.PostJsonAsync("/api/invitations/accept", new { token = raw });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        var note = Assert.Single(await s.NewDbContext().Notifications
            .Where(n => n.Type == "MemberJoined").ToListAsync());
        Assert.Equal(admin.Id, note.RecipientUserId);
        Assert.Equal(1, await s.CountAsync<WebhookDeliveryOutbox>(o => o.EventType == "MemberJoined"));
    }

    // ---- dispatch ----------------------------------------------------------

    [Fact]
    public async Task Test_SendsAPingAndRecordsSuccess()
    {
        var handler = new StubWebhookHandler();
        await using var s = await _database.CreateScenarioAsync(webhookHandler: handler);
        var (admin, _, ws) = await SeedAsync(s);
        using var client = await s.AsUserAsync(admin);
        var id = await CreateEndpointAsync(client, ws.Id, "slack", SlackUrl);

        var response = await client.PostJsonAsync($"{Base(ws.Id)}/{id}/test", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Single(handler.Requests);

        var deliveries = await client.GetJsonAsync<JsonElement>($"{Base(ws.Id)}/{id}/deliveries");
        Assert.Equal("TestPing", deliveries[0].GetProperty("eventType").GetString());

        var endpoint = await s.NewDbContext().WorkspaceWebhookEndpoints.SingleAsync();
        Assert.NotNull(endpoint.LastSuccessAt);
    }

    [Fact]
    public async Task Runner_OnServerError_BacksOffAndNeverLeaksTheUrl()
    {
        var handler = new StubWebhookHandler { Status = HttpStatusCode.InternalServerError };
        await using var s = await _database.CreateScenarioAsync(webhookHandler: handler);
        var (admin, _, ws) = await SeedAsync(s);
        using var client = await s.AsUserAsync(admin);
        await CreateEndpointAsync(client, ws.Id, "slack", SlackUrl, "TaskAssigned");
        await s.Services.GetRequiredService<IExternalAlertSink>().PublishAsync(
            new ExternalAlertMessage(ws.Id, "TaskAssigned", "t", "m", null, [admin.Id], null, "k1"));

        using var scope = s.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<WebhookOutboxRunner>();
        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        Assert.Equal(1, await runner.ProcessBatchAsync(now));

        var row = await s.NewDbContext().WebhookDeliveryOutbox.SingleAsync();
        Assert.Equal("Pending", row.Status);
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal(500, row.LastHttpStatus);
        Assert.True(row.NextAttemptAt >= now.AddSeconds(4));
        Assert.DoesNotContain("secretsecret", row.LastError ?? string.Empty);

        // Not due yet: a second pass at the same instant does nothing.
        Assert.Equal(0, await runner.ProcessBatchAsync(now));
    }

    [Fact]
    public async Task Runner_StopsAfterMaxAttempts_AndMarksTheRowFailed()
    {
        var handler = new StubWebhookHandler { Status = HttpStatusCode.BadGateway };
        await using var s = await _database.CreateScenarioAsync(webhookHandler: handler);
        var (admin, _, ws) = await SeedAsync(s);
        using var client = await s.AsUserAsync(admin);
        await CreateEndpointAsync(client, ws.Id, "discord", DiscordUrl, "TaskAssigned");
        await s.Services.GetRequiredService<IExternalAlertSink>().PublishAsync(
            new ExternalAlertMessage(ws.Id, "TaskAssigned", "t", "m", null, [admin.Id], null, "k2"));

        using var scope = s.Services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<WebhookOutboxRunner>();
        var future = DateTimeOffset.UtcNow.AddDays(1);
        for (var i = 0; i < 5; i++)
        {
            await runner.ProcessBatchAsync(future.AddHours(i));
        }

        var row = await s.NewDbContext().WebhookDeliveryOutbox.SingleAsync();
        Assert.Equal("Failed", row.Status);
        Assert.Equal(5, row.AttemptCount);
    }

    [Fact]
    public async Task CircuitBreaker_DisablesTheEndpointAtTenFailures_AndReEnableResetsTheCounter()
    {
        var handler = new StubWebhookHandler { Status = HttpStatusCode.InternalServerError };
        await using var s = await _database.CreateScenarioAsync(webhookHandler: handler);
        var (admin, _, ws) = await SeedAsync(s);
        using var client = await s.AsUserAsync(admin);
        var id = await CreateEndpointAsync(client, ws.Id, "slack", SlackUrl, "TaskAssigned");

        var sink = s.Services.GetRequiredService<IExternalAlertSink>();
        for (var i = 0; i < 10; i++)
        {
            await sink.PublishAsync(new ExternalAlertMessage(
                ws.Id, "TaskAssigned", "t", "m", null, [admin.Id], null, $"cb{i}"));
        }

        using (var scope = s.Services.CreateScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<WebhookOutboxRunner>();
            await runner.ProcessBatchAsync(DateTimeOffset.UtcNow.AddDays(1));
        }

        var endpoint = await s.NewDbContext().WorkspaceWebhookEndpoints.SingleAsync();
        Assert.False(endpoint.IsActive);
        Assert.True(endpoint.FailureCount >= 10);

        var reenable = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"{Base(ws.Id)}/{id}")
        {
            Content = JsonContent.Create(new { isActive = true }),
        });
        Assert.Equal(HttpStatusCode.OK, reenable.StatusCode);

        var after = await s.NewDbContext().WorkspaceWebhookEndpoints.SingleAsync();
        Assert.True(after.IsActive);
        Assert.Equal(0, after.FailureCount);
    }
}

