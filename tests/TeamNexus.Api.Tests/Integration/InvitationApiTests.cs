using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 11 §3 — workspace invitations: creating one (and its email), listing, cancelling, and the
/// two-step accept flow (<c>preview</c> anonymous, <c>accept</c> authenticated).
/// <para>
/// The suite runs with <c>Email:ApiKey = " "</c> (see <see cref="TeamNexusApiFactory"/>), so every
/// send goes through <c>NullEmailSender</c>: real mail is impossible in CI, yet the
/// <c>email_messages</c> audit row — the thing the rate-limit and the "Gửi lại" flow depend on — is
/// written exactly as in production.
/// </para>
/// <para>
/// Two invariants get their own tests because they are the security properties of the feature:
/// the raw token is never persisted (only its SHA-256 hash), and an invitation may only be accepted
/// by the account whose email it was addressed to.
/// </para>
/// </summary>
public sealed class InvitationApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public InvitationApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- create ------------------------------------------------------------

    [Fact]
    public async Task Invite_ByManager_CreatesAPendingInvitationAndSendsTheEmail()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/invitations",
            new { email = "invitee@example.test" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<InvitationDto>();
        Assert.NotNull(created);
        Assert.Equal("invitee@example.test", created!.InvitedEmail);
        Assert.Equal("Member", created.InvitedRole);
        Assert.Equal("Pending", created.Status);
        Assert.True(created.EmailSent, "NullEmailSender must report success so the flow stays exercisable offline.");

        // Seven days by default (Email:InvitationExpiryDays).
        var lifetime = created.ExpiresAt - created.CreatedAt;
        Assert.True(lifetime > TimeSpan.FromDays(6.9) && lifetime < TimeSpan.FromDays(7.1),
            $"expected ~7 days, got {lifetime}");

        await using var db = scenario.NewDbContext();

        // The raw token is emailed, never stored — only its hash.
        var stored = await db.WorkspaceInvitations.SingleAsync(i => i.Id == created.Id);
        Assert.Equal(64, stored.TokenHash.Length);
        Assert.DoesNotContain("=", stored.TokenHash);

        var email = await db.EmailMessages.SingleAsync(m => m.WorkspaceId == workspace.Id);
        Assert.Equal("WorkspaceInvitation", email.Kind);
        Assert.Equal("invitee@example.test", email.ToEmail);
        Assert.Equal(EmailMessageStatus.Sent, email.Status);

        // R11: the audit preview must never carry the accept token.
        Assert.DoesNotContain("token", email.BodyPreview, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invite_ByAPlainMember_IsForbidden()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, member) = await SeedWorkspaceAsync(scenario);
        using var client = await scenario.AsUserAsync(member);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/invitations",
            new { email = "nope@example.test" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var db = scenario.NewDbContext();
        Assert.Equal(0, await db.WorkspaceInvitations.CountAsync());
    }

    [Fact]
    public async Task Invite_ByAUserOutsideTheWorkspace_IsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (_, workspace, _) = await SeedWorkspaceAsync(scenario);
        var outsider = await scenario.CreateUserAsync("Người ngoài");
        using var client = await scenario.AsUserAsync(outsider);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/invitations",
            new { email = "nope@example.test" });

        // 404 rather than 403: the caller must not learn that the workspace exists.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("a@")]
    [InlineData("@b.com")]
    [InlineData("a@b")]
    [InlineData("a b@c.com")]
    [InlineData("")]
    public async Task Invite_WithAMalformedEmail_IsRejected(string email)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/invitations",
            new { email });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var db = scenario.NewDbContext();
        Assert.Equal(0, await db.WorkspaceInvitations.CountAsync());
    }

    [Fact]
    public async Task Invite_NormalizesTheEmailToLowerCase()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var created = await (await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/invitations",
            new { email = "  Foo@Bar.COM  " })).Content.ReadFromJsonAsync<InvitationDto>();

        Assert.Equal("foo@bar.com", created!.InvitedEmail);
    }

    [Fact]
    public async Task Invite_AnExistingMember_IsARejectedNoOp()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, member) = await SeedWorkspaceAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/invitations",
            new { email = member.Email });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invite_TwiceWhilePending_IsAConflictNotADatabaseError()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var url = $"/api/workspaces/{workspace.Id}/invitations";
        var first = await client.PostJsonAsync(url, new { email = "dup@example.test" });
        var second = await client.PostJsonAsync(url, new { email = "dup@example.test" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        await using var db = scenario.NewDbContext();
        Assert.Equal(1, await db.WorkspaceInvitations.CountAsync());
    }

    [Fact]
    public async Task Invite_AgainAfterACancel_Succeeds()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var url = $"/api/workspaces/{workspace.Id}/invitations";
        var created = await (await client.PostJsonAsync(url, new { email = "again@example.test" }))
            .Content.ReadFromJsonAsync<InvitationDto>();

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"{url}/{created!.Id}")).StatusCode);

        // The partial unique index only covers Pending rows, so re-inviting must be allowed.
        Assert.Equal(
            HttpStatusCode.Created,
            (await client.PostJsonAsync(url, new { email = "again@example.test" })).StatusCode);
    }

    [Theory]
    [InlineData("Boss")]
    [InlineData("1")]
    [InlineData("99")]
    public async Task Invite_WithAnUnknownRole_IsRejected(string role)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/invitations",
            new { email = "role@example.test", role });

        // "1" is the Phase 10 BUG-1 shape: Enum.TryParse would map it to Manager and succeed.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invite_WithAnExplicitRole_StoresItForTheAcceptStep()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/invitations",
            // NOT the seeded manager's own address: an existing member is rejected with 400, and an
            // error body ("{ error }") would still deserialize into the DTO as all-nulls — which is
            // how this test first "passed" its way into a red CI run.
            new { email = "explicit.role@example.test", role = "manager" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<InvitationDto>();
        Assert.Equal("Manager", created!.InvitedRole);
        Assert.Equal("explicit.role@example.test", created.InvitedEmail);
    }

    [Fact]
    public async Task Invite_WithoutTheAntiforgeryHeader_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        // Send through the raw client: TestHttpClient re-attaches the header on every mutating verb
        // (the T3 trap recorded in Phase 10 §2.7).
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/workspaces/{workspace.Id}/invitations")
        {
            Content = JsonContent.Create(new { email = "csrf@example.test" }),
        };

        var response = await client.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- list & cancel -----------------------------------------------------

    [Fact]
    public async Task List_ByAPlainMember_IsForbidden()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (_, workspace, member) = await SeedWorkspaceAsync(scenario);
        using var client = await scenario.AsUserAsync(member);

        var response = await client.GetAsync($"/api/workspaces/{workspace.Id}/invitations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_ReportsAnInvitationThatHasPassedItsExpiryAsExpired()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);

        // Seeded with a known raw token so the API path is the only thing under test.
        const string raw = "expired-token";
        await SeedInvitationAsync(scenario, workspace, manager, "stale@example.test", raw, DateTimeOffset.UtcNow.AddDays(-1));

        using var client = await scenario.AsUserAsync(manager);
        var list = await client.GetJsonAsync<List<InvitationDto>>($"/api/workspaces/{workspace.Id}/invitations");

        Assert.NotNull(list);
        var item = Assert.Single(list!);
        Assert.Equal("Expired", item.Status);

        await using var db = scenario.NewDbContext();
        Assert.Equal(
            InvitationStatus.Expired,
            (await db.WorkspaceInvitations.SingleAsync(i => i.InvitedEmail == "stale@example.test")).Status);
    }

    [Fact]
    public async Task Cancel_ByManager_MarksTheInvitationCancelled()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var created = await (await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/invitations",
            new { email = "cancel@example.test" })).Content.ReadFromJsonAsync<InvitationDto>();

        var response = await client.DeleteAsync($"/api/workspaces/{workspace.Id}/invitations/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var db = scenario.NewDbContext();
        Assert.Equal(
            InvitationStatus.Cancelled,
            (await db.WorkspaceInvitations.SingleAsync(i => i.Id == created.Id)).Status);

        // Workspace-level event: board_id IS NULL (Phase 10 D5).
        var activity = await db.Activities
            .SingleAsync(a => a.Action == ObserverActivityActions.InvitationCancelled);
        Assert.Null(activity.BoardId);
        Assert.Equal("Workspace", activity.EntityType);
    }

    [Fact]
    public async Task Cancel_AnInvitationOfAnotherWorkspace_IsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspaceA, _) = await SeedWorkspaceAsync(scenario);
        var other = await scenario.CreateUserAsync("Chủ workspace B");
        var workspaceB = await scenario.CreateWorkspaceAsync(other, "Workspace B");

        using var client = await scenario.AsUserAsync(manager);
        var created = await (await client.PostJsonAsync(
            $"/api/workspaces/{workspaceA.Id}/invitations",
            new { email = "cross@example.test" })).Content.ReadFromJsonAsync<InvitationDto>();

        var response = await client.DeleteAsync($"/api/workspaces/{workspaceB.Id}/invitations/{created!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_AnAcceptedInvitation_IsAConflict()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);

        const string raw = "accepted-token";
        var invitation = await SeedInvitationAsync(
            scenario, workspace, manager, "accepted@example.test", raw, DateTimeOffset.UtcNow.AddDays(3));
        invitation.Status = InvitationStatus.Accepted;

        await using (var db = scenario.NewDbContext())
        {
            db.WorkspaceInvitations.Update(invitation);
            await db.SaveChangesAsync();
        }

        using var client = await scenario.AsUserAsync(manager);
        var response = await client.DeleteAsync($"/api/workspaces/{workspace.Id}/invitations/{invitation.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_IsIdempotentForAnAlreadyCancelledInvitation()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var created = await (await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/invitations",
            new { email = "idem@example.test" })).Content.ReadFromJsonAsync<InvitationDto>();

        var url = $"/api/workspaces/{workspace.Id}/invitations/{created!.Id}";

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(url)).StatusCode);
    }

    // ---- preview -----------------------------------------------------------

    [Fact]
    public async Task Preview_IsAnonymousAndDisclosesOnlyTheInvitationFacts()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);

        const string raw = "preview-token";
        await SeedInvitationAsync(scenario, workspace, manager, "preview@example.test", raw, DateTimeOffset.UtcNow.AddDays(2));

        using var anonymous = await scenario.AnonymousAsync();
        var response = await anonymous.GetAsync($"/api/invitations/preview?token={raw}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var keys = document.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Equal(
            ["workspaceId", "workspaceName", "invitedEmail", "invitedRole", "invitedByName", "expiresAt", "status"],
            keys);
        Assert.Equal("preview@example.test", document.RootElement.GetProperty("invitedEmail").GetString());
    }

    [Fact]
    public async Task Preview_WithAGarbageToken_IsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        using var anonymous = await scenario.AnonymousAsync();

        var response = await anonymous.GetAsync("/api/invitations/preview?token=khong-ton-tai");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Preview_OfAnExpiredInvitation_IsGoneAndNormalizesTheRow()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);

        const string raw = "expired-preview-token";
        await SeedInvitationAsync(scenario, workspace, manager, "gone@example.test", raw, DateTimeOffset.UtcNow.AddHours(-1));

        using var anonymous = await scenario.AnonymousAsync();
        var response = await anonymous.GetAsync($"/api/invitations/preview?token={raw}");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);

        await using var db = scenario.NewDbContext();
        Assert.Equal(
            InvitationStatus.Expired,
            (await db.WorkspaceInvitations.SingleAsync(i => i.InvitedEmail == "gone@example.test")).Status);
    }

    // ---- accept ------------------------------------------------------------

    [Fact]
    public async Task Accept_ByTheInvitedAccount_CreatesTheMembershipWithTheInvitedRole()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);

        const string raw = "accept-token";
        await SeedInvitationAsync(
            scenario, workspace, manager, "joiner@example.test", raw, DateTimeOffset.UtcNow.AddDays(3),
            WorkspaceRole.Manager);

        var joiner = await scenario.CreateUserAsync("Người được mời", "joiner@example.test");
        using var client = await scenario.AsUserAsync(joiner);

        var response = await client.PostJsonAsync("/api/invitations/accept", new { token = raw });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<AcceptDto>();
        Assert.Equal(workspace.Id, result!.WorkspaceId);
        Assert.Equal("Manager", result.Role);
        Assert.False(result.AlreadyMember);

        await using var db = scenario.NewDbContext();
        var membership = await db.WorkspaceMembers
            .SingleAsync(wm => wm.WorkspaceId == workspace.Id && wm.UserId == joiner.Id);
        Assert.Equal(WorkspaceRole.Manager, membership.Role);

        var invitation = await db.WorkspaceInvitations.SingleAsync(i => i.InvitedEmail == "joiner@example.test");
        Assert.Equal(InvitationStatus.Accepted, invitation.Status);
        Assert.Equal(joiner.Id, invitation.AcceptedByUserId);
        Assert.NotNull(invitation.AcceptedAt);

        var activity = await db.Activities.SingleAsync(a => a.Action == ObserverActivityActions.InvitationAccepted);
        Assert.Null(activity.BoardId);
    }

    [Fact]
    public async Task Accept_ByADifferentAccount_IsForbiddenAndLeavesTheInvitationPending()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);

        const string raw = "mismatch-token";
        await SeedInvitationAsync(scenario, workspace, manager, "target@example.test", raw, DateTimeOffset.UtcNow.AddDays(3));

        var attacker = await scenario.CreateUserAsync("Người khác", "attacker@example.test");
        using var client = await scenario.AsUserAsync(attacker);

        var response = await client.PostJsonAsync("/api/invitations/accept", new { token = raw });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var db = scenario.NewDbContext();
        Assert.Equal(0, await db.WorkspaceMembers.CountAsync(wm => wm.UserId == attacker.Id));
        Assert.Equal(
            InvitationStatus.Pending,
            (await db.WorkspaceInvitations.SingleAsync(i => i.InvitedEmail == "target@example.test")).Status);
    }

    [Fact]
    public async Task Accept_IsIdempotentAndNeverOverwritesAnExistingRole()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);

        const string raw = "already-member-token";
        // Invited as Admin, but the account is already an ordinary Member: accepting must not promote.
        await SeedInvitationAsync(
            scenario, workspace, manager, "already@example.test", raw, DateTimeOffset.UtcNow.AddDays(3),
            WorkspaceRole.Admin);

        var existing = await scenario.CreateUserAsync("Thành viên cũ", "already@example.test");
        await using (var db = scenario.NewDbContext())
        {
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspace.Id,
                UserId = existing.Id,
                Role = WorkspaceRole.Member,
            });
            await db.SaveChangesAsync();
        }

        using var client = await scenario.AsUserAsync(existing);
        var result = await (await client.PostJsonAsync("/api/invitations/accept", new { token = raw }))
            .Content.ReadFromJsonAsync<AcceptDto>();

        Assert.True(result!.AlreadyMember);
        Assert.Equal("Member", result.Role);

        await using var verify = scenario.NewDbContext();
        var membership = await verify.WorkspaceMembers
            .SingleAsync(wm => wm.WorkspaceId == workspace.Id && wm.UserId == existing.Id);
        Assert.Equal(WorkspaceRole.Member, membership.Role);
        Assert.Equal(
            InvitationStatus.Accepted,
            (await verify.WorkspaceInvitations.SingleAsync(i => i.InvitedEmail == "already@example.test")).Status);
    }

    [Fact]
    public async Task Accept_WithoutBeingSignedIn_IsUnauthorized()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);

        const string raw = "anonymous-token";
        await SeedInvitationAsync(scenario, workspace, manager, "anon@example.test", raw, DateTimeOffset.UtcNow.AddDays(3));

        using var anonymous = await scenario.AnonymousAsync();
        var response = await anonymous.PostJsonAsync("/api/invitations/accept", new { token = raw });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Accept_AnExpiredToken_IsGone()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);

        const string raw = "late-token";
        await SeedInvitationAsync(scenario, workspace, manager, "late@example.test", raw, DateTimeOffset.UtcNow.AddMinutes(-5));

        var late = await scenario.CreateUserAsync("Người đến muộn", "late@example.test");
        using var client = await scenario.AsUserAsync(late);

        var response = await client.PostJsonAsync("/api/invitations/accept", new { token = raw });

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);

        await using var db = scenario.NewDbContext();
        Assert.Equal(0, await db.WorkspaceMembers.CountAsync(wm => wm.UserId == late.Id));
    }

    [Fact]
    public async Task Accept_ACancelledToken_IsGone()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);
        using var client = await scenario.AsUserAsync(manager);

        var created = await (await client.PostJsonAsync(
            $"/api/workspaces/{workspace.Id}/invitations",
            new { email = "cancelled@example.test" })).Content.ReadFromJsonAsync<InvitationDto>();

        await client.DeleteAsync($"/api/workspaces/{workspace.Id}/invitations/{created!.Id}");

        // The raw token only exists in the email; re-inviting gives a fresh one, so drive the raw
        // value through a seeded row instead of trying to recover it.
        const string raw = "cancelled-token";
        await SeedInvitationAsync(
            scenario, workspace, manager, "cancelled2@example.test", raw, DateTimeOffset.UtcNow.AddDays(3),
            status: InvitationStatus.Cancelled);

        var joiner = await scenario.CreateUserAsync("Người bị huỷ", "cancelled2@example.test");
        using var joinerClient = await scenario.AsUserAsync(joiner);

        var response = await joinerClient.PostJsonAsync("/api/invitations/accept", new { token = raw });

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task Accept_WithAGarbageToken_IsNotFound()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Ai đó");
        using var client = await scenario.AsUserAsync(user);

        var response = await client.PostJsonAsync("/api/invitations/accept", new { token = "rac" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Accept_NeverWritesTheRawTokenIntoTheActivityLogOrTheEmailAudit()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);

        const string raw = "secret-raw-token";
        await SeedInvitationAsync(scenario, workspace, manager, "secret@example.test", raw, DateTimeOffset.UtcNow.AddDays(3));

        var joiner = await scenario.CreateUserAsync("Người nhận", "secret@example.test");
        using var client = await scenario.AsUserAsync(joiner);
        await client.PostJsonAsync("/api/invitations/accept", new { token = raw });

        await using var db = scenario.NewDbContext();

        var payloads = await db.Activities.Select(a => a.Payload).ToListAsync();
        Assert.All(payloads, payload => Assert.DoesNotContain(raw, payload ?? string.Empty, StringComparison.Ordinal));

        var previews = await db.EmailMessages.Select(m => m.BodyPreview).ToListAsync();
        Assert.All(previews, preview => Assert.DoesNotContain(raw, preview, StringComparison.Ordinal));

        // And the API never echoes it back either.
        using var managerClient = await scenario.AsUserAsync(manager);
        var listJson = await managerClient.Http.GetStringAsync($"/api/workspaces/{workspace.Id}/invitations");
        Assert.DoesNotContain(raw, listJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Accept_WithAFailedEmailAttempt_StillCreatesTheMembership()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (manager, workspace, _) = await SeedWorkspaceAsync(scenario);

        const string raw = "offline-email-token";
        await SeedInvitationAsync(scenario, workspace, manager, "offline@example.test", raw, DateTimeOffset.UtcNow.AddDays(3));

        // The invitation row is real data and must survive an email outage, so acceptance does not
        // depend on any delivery state.
        var joiner = await scenario.CreateUserAsync("Người nhận", "offline@example.test");
        using var client = await scenario.AsUserAsync(joiner);

        var response = await client.PostJsonAsync("/api/invitations/accept", new { token = raw });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- helpers -----------------------------------------------------------

    private static async Task<(ApplicationUser Manager, Workspace Workspace, ApplicationUser Member)>
        SeedWorkspaceAsync(TestScenario scenario)
    {
        var manager = await scenario.CreateUserAsync("Quản lý", "manager@example.test");
        var member = await scenario.CreateUserAsync("Thành viên", "member@example.test");
        var workspace = await scenario.CreateWorkspaceAsync(
            manager,
            "Workspace Lời Mời",
            (member, WorkspaceRole.Member));

        return (manager, workspace, member);
    }

    /// <summary>
    /// Inserts an invitation with a <b>known raw token</b> — the API only ever returns the hash, so
    /// the accept path is exercised with a token the test also holds.
    /// </summary>
    private static async Task<WorkspaceInvitation> SeedInvitationAsync(
        TestScenario scenario,
        Workspace workspace,
        ApplicationUser invitedBy,
        string email,
        string rawToken,
        DateTimeOffset expiresAt,
        WorkspaceRole role = WorkspaceRole.Member,
        InvitationStatus status = InvitationStatus.Pending)
    {
        await using var db = scenario.NewDbContext();

        var invitation = new WorkspaceInvitation
        {
            WorkspaceId = workspace.Id,
            InvitedEmail = email,
            InvitedRole = role,
            TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))),
            InvitedByUserId = invitedBy.Id,
            Status = status,
            ExpiresAt = expiresAt,
        };

        db.WorkspaceInvitations.Add(invitation);
        await db.SaveChangesAsync();

        return invitation;
    }

    private sealed record InvitationDto
    {
        public Guid Id { get; init; }
        public string? InvitedEmail { get; init; }
        public string? InvitedRole { get; init; }
        public string? Status { get; init; }
        public string? InvitedByName { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public Guid? AcceptedByUserId { get; init; }
        public DateTimeOffset? AcceptedAt { get; init; }
        public bool EmailSent { get; init; }
    }

    private sealed record AcceptDto
    {
        public Guid WorkspaceId { get; init; }
        public string? Role { get; init; }
        public bool AlreadyMember { get; init; }
    }
}
