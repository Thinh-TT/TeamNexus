using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 11 §4.1 — "quick email": a Manager composes a short notice and the API sends it to selected
/// members over the audited email gateway.
/// <para>
/// The suite runs with <c>Email:ApiKey = " "</c>, so every send goes through <c>NullEmailSender</c>:
/// no real mail can leave CI, but the <c>email_messages</c> rows that the quota and the UI depend on
/// are written exactly as in production.
/// </para>
/// </summary>
public sealed class QuickEmailApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public QuickEmailApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task Send_ByManager_ReachesEveryHumanRecipient()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, first, second) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/quick-email",
            new
            {
                subject = "Họp nhóm 9h",
                body = "Mọi người có mặt đúng giờ nhé.",
                recipientUserIds = new[] { first.Id, second.Id },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<QuickEmailResultDto>();
        Assert.Equal(2, result!.Requested);
        Assert.Equal(2, result.Sent);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Errors);

        await using var db = scenario.NewDbContext();
        var sent = await db.EmailMessages
            .Where(m => m.WorkspaceId == workspace.Id)
            .ToListAsync();

        Assert.Equal(2, sent.Count);
        Assert.All(sent, m =>
        {
            Assert.Equal("QuickEmail", m.Kind);
            Assert.Equal(EmailMessageStatus.Sent, m.Status);
            Assert.Equal("Họp nhóm 9h", m.Subject);
        });
        Assert.Contains(sent, m => m.ToEmail == first.Email);
        Assert.Contains(sent, m => m.ToEmail == second.Email);
    }

    [Fact]
    public async Task Send_SkipsTheSenderThemselves()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, other, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var result = await (await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/quick-email",
            new
            {
                subject = "Nhắc việc",
                body = "Còn 2 task chưa xong.",
                recipientUserIds = new[] { manager.Id, other.Id },
            })).Content.ReadFromJsonAsync<QuickEmailResultDto>();

        // The Manager is never counted: sending a notice to yourself is not a send.
        Assert.Equal(1, result!.Requested);
        Assert.Equal(1, result.Sent);

        await using var db = scenario.NewDbContext();
        Assert.False(await db.EmailMessages.AnyAsync(m => m.ToEmail == manager.Email));
    }

    [Fact]
    public async Task Send_ByAPlainMember_IsForbidden()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (_, workspace, member, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(member);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/quick-email",
            new
            {
                subject = "Thử",
                body = "Không được phép.",
                recipientUserIds = new[] { member.Id },
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var db = scenario.NewDbContext();
        Assert.Equal(0, await db.EmailMessages.CountAsync());
    }

    [Fact]
    public async Task Send_ByAUserOutsideTheWorkspace_IsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (_, workspace, _, _) = await SeedAsync(scenario);
        var outsider = await scenario.CreateUserAsync("Người ngoài");
        using var client = await scenario.AsUserAsync(outsider);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/quick-email",
            new { subject = "Thử", body = "Ngoài workspace.", recipientUserIds = new[] { outsider.Id } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("", "nội dung")]
    [InlineData("   ", "nội dung")]
    [InlineData("tiêu đề", "")]
    [InlineData("tiêu đề", "   ")]
    public async Task Send_WithABlankSubjectOrBody_IsRejected(string subject, string body)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, other, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/quick-email",
            new { subject, body, recipientUserIds = new[] { other.Id } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var db = scenario.NewDbContext();
        Assert.Equal(0, await db.EmailMessages.CountAsync());
    }

    [Fact]
    public async Task Send_WithATooLongSubject_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, other, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/quick-email",
            new
            {
                subject = new string('x', 201),
                body = "nội dung",
                recipientUserIds = new[] { other.Id },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The boundary itself is accepted (200 chars is the column limit).
        var boundary = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/quick-email",
            new
            {
                subject = new string('x', 200),
                body = "nội dung",
                recipientUserIds = new[] { other.Id },
            });

        Assert.Equal(HttpStatusCode.OK, boundary.StatusCode);
    }

    [Fact]
    public async Task Send_WithATooLongBody_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, other, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/quick-email",
            new
            {
                subject = "Tiêu đề",
                body = new string('x', 8001),
                recipientUserIds = new[] { other.Id },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Send_WithoutRecipients_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/quick-email",
            new { subject = "Tiêu đề", body = "Nội dung", recipientUserIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Send_WithARecipientOutsideTheWorkspace_IsRejectedAndSendsNothing()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, other, _) = await SeedAsync(scenario);
        var stranger = await scenario.CreateUserAsync("Người lạ");
        using var client = await scenario.AsUserAsync(manager);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/quick-email",
            new
            {
                subject = "Tiêu đề",
                body = "Nội dung",
                // One valid recipient is not enough: the bad id must fail the whole call, otherwise a
                // typo would silently send a message about a workspace to the wrong person's address
                // while the UI reported success.
                recipientUserIds = new[] { other.Id, stranger.Id },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var db = scenario.NewDbContext();
        Assert.Equal(0, await db.EmailMessages.CountAsync());
    }

    [Fact]
    public async Task Send_ToTheAiAgent_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        // Materialise the agent through the members endpoint (it is created lazily).
        var members = await client.GetJsonAsync<List<MemberDto>>($"/api/workspaces/{workspace.Id}/members");
        var agentId = members!.Single(m => m.MemberType == "ai_agent").UserId;

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/quick-email",
            new { subject = "Tiêu đề", body = "Nội dung", recipientUserIds = new[] { agentId } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Send_WhenTheHourlyQuotaIsExhausted_IsTooManyRequests()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, other, _) = await SeedAsync(scenario);

        // Seed the quota directly: 100 rows is the default Email:MaxEmailsPerHourPerWorkspace, and
        // creating them through the API would take 100 round-trips.
        await using (var db = scenario.NewDbContext())
        {
            for (var i = 0; i < 100; i++)
            {
                db.EmailMessages.Add(new EmailMessage
                {
                    WorkspaceId = workspace.Id,
                    SentByUserId = manager.Id,
                    Kind = "QuickEmail",
                    ToEmail = $"bulk{i}@example.test",
                    Subject = "Bulk",
                    BodyPreview = "Bulk",
                    Status = EmailMessageStatus.Sent,
                });
            }

            await db.SaveChangesAsync();
        }

        using var client = await scenario.AsUserAsync(manager);
        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/quick-email",
            new { subject = "Tiêu đề", body = "Nội dung", recipientUserIds = new[] { other.Id } });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Send_WithoutTheAntiforgeryHeader_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, other, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/workspaces/{workspace.Id}/quick-email")
        {
            Content = JsonContent.Create(new
            {
                subject = "Tiêu đề",
                body = "Nội dung",
                recipientUserIds = new[] { other.Id },
            }),
        };

        var response = await client.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Send_StoresOnlyAShortPreviewNotTheWholeBody()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, other, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var body = new string('y', 8000);
        await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/quick-email",
            new { subject = "Thông báo dài", body, recipientUserIds = new[] { other.Id } });

        await using var db = scenario.NewDbContext();
        var stored = await db.EmailMessages.SingleAsync(m => m.ToEmail == other.Email);

        // The audit row is a pointer, not a copy of the message (and never a token, see §3).
        Assert.True(stored.BodyPreview.Length <= 500, $"preview was {stored.BodyPreview.Length}");
        Assert.NotEqual(body, stored.BodyPreview);
    }

    // ---- helpers -----------------------------------------------------------

    private static async Task<(
        ApplicationUser Manager, Workspace Workspace, ApplicationUser First, ApplicationUser Second)>
        SeedAsync(TestScenario scenario)
    {
        var manager = await scenario.CreateUserAsync("Quản lý", "qm.manager@example.test");
        var first = await scenario.CreateUserAsync("Thành viên A", "qm.a@example.test");
        var second = await scenario.CreateUserAsync("Thành viên B", "qm.b@example.test");

        var workspace = await scenario.CreateWorkspaceAsync(
            manager,
            "Workspace Email Nhanh",
            (first, WorkspaceRole.Member),
            (second, WorkspaceRole.Member));

        return (manager, workspace, first, second);
    }

    private sealed record QuickEmailResultDto
    {
        public int Requested { get; init; }
        public int Sent { get; init; }
        public int Failed { get; init; }
        public List<string> Errors { get; init; } = [];
    }

    private sealed record MemberDto
    {
        public Guid UserId { get; init; }
        public string? MemberType { get; init; }
    }
}
