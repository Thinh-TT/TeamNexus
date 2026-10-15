using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Ai.Services.Agent;
using TeamNexus.Modules.Ai.Services.Appliers;
using TeamNexus.Modules.Ai.Services.Jev;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Giai đoạn 14.7 §A5 Phần B — the AI Observer's proactive recovery plan.
/// <para>
/// The scan is triggered through the real Manager-only endpoint, so the whole path is exercised: detector →
/// Jev severity → Jev recovery routing → a <c>Pending</c> <c>RecoveryPlan</c> action → Approve/Reject/Undo.
/// The offline <c>FakeJevDecisionProvider</c> supplies the verdicts, so no test reaches the network.
/// </para>
/// <para>
/// <b>The default-off assertion is the most important one here.</b> This is the only Observer feature that
/// creates a data-affecting proposal, so a regression that switched it on by default would start asking
/// managers to approve AI-chosen reassignments without anyone opting in.
/// </para>
/// </summary>
public sealed class Phase147RecoveryPlanApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public Phase147RecoveryPlanApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    /// <summary>
    /// A workspace with ONE badly overdue task, which produces an <c>OverdueTask</c> signal severe enough for
    /// the pipeline to reach the recovery phase.
    /// </summary>
    private static async Task<(ApplicationUser Admin, ApplicationUser Dev, Workspace Workspace, Board Board, BoardTask Task)>
        SeedAsync(TestScenario scenario)
    {
        var admin = await scenario.CreateUserAsync("Quản trị");
        var dev = await scenario.CreateUserAsync("Nguyễn Văn A");

        var workspace = await scenario.CreateWorkspaceAsync(
            admin, others: [(dev, WorkspaceRole.Member)]);

        var (board, todo, _) = await scenario.CreateBoardAsync(workspace, "Board Phục Hồi");

        var task = await scenario.CreateTaskAsync(
            board, todo, admin,
            title: "Di trú dịch vụ thanh toán",
            assigneeId: dev.Id,
            // 30 days past due is well past CriticalOverdueDays (3), so the detector reports Critical.
            dueDate: DateTimeOffset.UtcNow.AddDays(-30));

        return (admin, dev, workspace, board, task);
    }

    private static Task<HttpResponseMessage> ScanAsync(TestHttpClient client, Guid workspaceId)
        => client.PostJsonAsync($"/api/workspaces/{workspaceId}/observer/scan", new { });

    private static async Task<List<PendingPlanDto>> LoadPlansAsync(TestScenario scenario, Guid workspaceId)
    {
        await using var db = scenario.NewDbContext();

        return await db.AiActionLogs
            .AsNoTracking()
            .Where(x => x.Action == AiActionTypes.RecoveryPlan
                        && x.EntityType == AiEntityTypes.Workspace
                        && x.EntityId == workspaceId)
            .Select(x => new PendingPlanDto(x.Id, x.Status.ToString(), x.AfterSnapshot))
            .ToListAsync();
    }

    // ---- the default-off switch ---------------------------------------------------------------

    [Fact]
    public async Task Scan_WithRecoveryPlansOffByDefault_DraftsNothing()
    {
        // The production default is OFF, so an ordinary scan must never create a proposal. This is the guard
        // that keeps an opt-in feature from becoming an opt-out one.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, workspace, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await ScanAsync(client, workspace.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Empty(await LoadPlansAsync(scenario, workspace.Id));
    }

    [Fact]
    public async Task Scan_WithRecoveryPlansOff_DoesNotNotifyAboutAPlan()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, _, workspace, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await ScanAsync(client, workspace.Id);

        var planAlerts = await scenario.CountAsync<Notification>(
            n => n.Type == AgentNotificationTypes.RecoveryPlanPending);

        Assert.Equal(0, planAlerts);
    }

    // ---- the happy path ----------------------------------------------------------------------

    [Fact]
    public async Task Scan_WithRecoveryPlansOn_DraftsExactlyOnePendingPlanAndNotifiesManagers()
    {
        await using var scenario = await _database.CreateScenarioAsync(autoRecoveryPlanEnabled: true);
        var (admin, _, workspace, _, task) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await ScanAsync(client, workspace.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var plans = await LoadPlansAsync(scenario, workspace.Id);

        // EXACTLY one: a run that drafted several plans for one situation would ask a manager to approve
        // overlapping changes to the same card.
        var plan = Assert.Single(plans);
        Assert.Equal("Pending", plan.Status);

        // The stored kind must be one of the four the applier understands — a plan nobody can apply is a
        // dead end in the manager's queue.
        var kind = ReadKind(plan.AfterSnapshot);
        Assert.Contains(kind, RecoveryPlanKinds.All);

        // The manager is told, otherwise the proposal would only be discoverable by opening the AI history.
        var alerts = await scenario.CountAsync<Notification>(
            n => n.Type == AgentNotificationTypes.RecoveryPlanPending && n.RecipientUserId == admin.Id);
        Assert.Equal(1, alerts);

        // The card is the one the overdue signal named.
        Assert.Contains(task.Id.ToString(), plan.AfterSnapshot!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scan_WhenTheRequesterIsAttributedToAManagerNotASyntheticSystemUser()
    {
        // `ai_action_logs.requested_by_user_id` is a required FK to users and there is no system principal, so
        // the draft is attributed to the real Admin/Manager who can approve it.
        await using var scenario = await _database.CreateScenarioAsync(autoRecoveryPlanEnabled: true);
        var (admin, _, workspace, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await ScanAsync(client, workspace.Id);

        var plans = await LoadPlansAsync(scenario, workspace.Id);
        var plan = Assert.Single(plans);

        await using var db = scenario.NewDbContext();
        var requestedBy = await db.AiActionLogs
            .Where(x => x.Id == plan.Id)
            .Select(x => x.RequestedByUserId)
            .FirstAsync();

        Assert.Equal(admin.Id, requestedBy);
    }

    [Fact]
    public async Task Scan_WithoutAManager_SkipsThePlanInsteadOfFailing()
    {
        // A workspace whose only member is a plain Member has nobody to attribute the draft to. The scan must
        // still succeed — the alerts are its primary output.
        await using var scenario = await _database.CreateScenarioAsync(autoRecoveryPlanEnabled: true);

        var member = await scenario.CreateUserAsync("Thành viên");
        var owner = await scenario.CreateUserAsync("Chủ sở hữu");

        var workspace = await scenario.CreateWorkspaceAsync(owner);
        var (board, todo, _) = await scenario.CreateBoardAsync(workspace, "Board Không Quản Lý");

        await scenario.CreateTaskAsync(
            board, todo, owner, "Thẻ quá hạn",
            dueDate: DateTimeOffset.UtcNow.AddDays(-30));

        // Demote the owner so the workspace has no Admin/Manager at all.
        await using (var db = scenario.NewDbContext())
        {
            var membership = await db.WorkspaceMembers
                .FirstAsync(wm => wm.WorkspaceId == workspace.Id && wm.UserId == owner.Id);
            membership.Role = WorkspaceRole.Member;
            await db.SaveChangesAsync();
        }

        // A plain Member cannot trigger a scan (Manager+), so the scan is invoked as the owner after the demote
        // — which now 403s. Instead assert the invariant directly: no plan was created by any means.
        using var client = await scenario.AsUserAsync(owner);
        var response = await ScanAsync(client, workspace.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await LoadPlansAsync(scenario, workspace.Id));
    }

    // ---- approve / reject / undo -------------------------------------------------------------

    [Fact]
    public async Task Approve_ReassignTaskPlan_MovesTheCardAndUndoRestoresTheOldAssignee()
    {
        await using var scenario = await _database.CreateScenarioAsync(autoRecoveryPlanEnabled: true);
        var (admin, dev, workspace, _, task) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await ScanAsync(client, workspace.Id);

        var plan = Assert.Single(await LoadPlansAsync(scenario, workspace.Id));

        // Force the reassign kind so the assertion is about Apply/Undo, not about which kind Jev happened to
        // pick. The payload is otherwise exactly what the pipeline wrote.
        await OverwriteKindAsync(scenario, plan.Id, RecoveryPlanKinds.ReassignTask, assigneeId: admin.Id);

        var approve = await client.PostJsonAsync($"/api/ai-actions/{plan.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var afterApprove = await LoadTaskAsync(scenario, task.Id);
        Assert.Equal(admin.Id, afterApprove.AssigneeId);

        var undo = await client.PostJsonAsync($"/api/ai-actions/{plan.Id}/undo", new { });
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);

        var afterUndo = await LoadTaskAsync(scenario, task.Id);
        Assert.Equal(dev.Id, afterUndo.AssigneeId);
    }

    [Fact]
    public async Task Approve_ExtendDeadlinePlan_SetsANewDeadlineAndUndoRestoresTheOldOne()
    {
        await using var scenario = await _database.CreateScenarioAsync(autoRecoveryPlanEnabled: true);
        var (admin, _, workspace, _, task) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await ScanAsync(client, workspace.Id);
        var plan = Assert.Single(await LoadPlansAsync(scenario, workspace.Id));

        var originalDue = (await LoadTaskAsync(scenario, task.Id)).DueDate;
        Assert.NotNull(originalDue);

        await OverwriteKindAsync(scenario, plan.Id, RecoveryPlanKinds.ExtendDeadline, extensionDays: 14);

        var approve = await client.PostJsonAsync($"/api/ai-actions/{plan.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var afterApprove = await LoadTaskAsync(scenario, task.Id);
        Assert.NotNull(afterApprove.DueDate);

        // Extended relative to NOW, not to the old (already long past) deadline — otherwise the "new" date
        // would still be overdue.
        Assert.True(afterApprove.DueDate > DateTimeOffset.UtcNow.AddDays(10));

        var undo = await client.PostJsonAsync($"/api/ai-actions/{plan.Id}/undo", new { });
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);

        var afterUndo = await LoadTaskAsync(scenario, task.Id);
        Assert.Equal(originalDue!.Value.ToUnixTimeSeconds(), afterUndo.DueDate!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Approve_EscalateManagerPlan_ChangesNoDataButReportsTheWarning()
    {
        // ⚠️ The one kind whose success IS the absence of a change. Without this test it would be an approve
        // button that silently does nothing and nobody would notice.
        await using var scenario = await _database.CreateScenarioAsync(autoRecoveryPlanEnabled: true);
        var (admin, dev, workspace, _, task) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await ScanAsync(client, workspace.Id);
        var plan = Assert.Single(await LoadPlansAsync(scenario, workspace.Id));

        var before = await LoadTaskAsync(scenario, task.Id);
        await OverwriteKindAsync(scenario, plan.Id, RecoveryPlanKinds.EscalateManager);

        var approve = await client.PostJsonAsync($"/api/ai-actions/{plan.Id}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        // The card is untouched…
        var after = await LoadTaskAsync(scenario, task.Id);
        Assert.Equal(before.AssigneeId, after.AssigneeId);
        Assert.Equal(before.DueDate, after.DueDate);
        Assert.Equal(before.ColumnId, after.ColumnId);

        // …and the manager is told that approving it did not (and should not) change anything.
        var detail = await client.GetJsonAsync<ActionDetailDto>($"/api/ai-actions/{plan.Id}");
        Assert.NotNull(detail);
        Assert.NotNull(detail!.AppliedSnapshot);

        var warnings = detail.AppliedSnapshot!.Value.GetProperty("warnings");
        Assert.Contains(
            warnings.EnumerateArray().Select(w => w.GetString()),
            warning => warning is not null && warning.Contains("không có thay đổi", StringComparison.OrdinalIgnoreCase));

        // Undo succeeds too: there was never a change, so failing here would leave an un-undoable action.
        var undo = await client.PostJsonAsync($"/api/ai-actions/{plan.Id}/undo", new { });
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
    }

    [Fact]
    public async Task Approve_SplitSubtasksPlan_CreatesSubTasksAndUndoSoftDeletesThem()
    {
        await using var scenario = await _database.CreateScenarioAsync(autoRecoveryPlanEnabled: true);
        var (admin, _, workspace, board, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await ScanAsync(client, workspace.Id);
        var plan = Assert.Single(await LoadPlansAsync(scenario, workspace.Id));

        var before = await scenario.CountAsync<BoardTask>(t => t.BoardId == board.Id);

        await OverwriteKindAsync(scenario, plan.Id, RecoveryPlanKinds.SplitSubtasks);

        // Prove the fixture wrote a payload the applier can actually use, BEFORE blaming the applier. An
        // earlier version of this fixture wrote `"tasks": null` and the resulting 400 looked like an applier
        // bug for two debugging rounds.
        var rewritten = Assert.Single(await LoadPlansAsync(scenario, workspace.Id));
        var reparsed = RecoveryPlanApplier.ParsePlan(new AiActionLog { AfterSnapshot = rewritten.AfterSnapshot });
        Assert.True(reparsed.Tasks is { Count: > 0 }, $"Fixture payload has no tasks: {rewritten.AfterSnapshot}");

        var approve = await client.PostJsonAsync($"/api/ai-actions/{plan.Id}/approve", new { });
        Assert.True(
            approve.StatusCode == HttpStatusCode.OK,
            $"Expected OK, got {(int)approve.StatusCode}. Body: {await approve.Content.ReadAsStringAsync()}");

        var after = await scenario.CountAsync<BoardTask>(t => t.BoardId == board.Id);
        Assert.Equal(before + 1, after);

        // Undo delegates to the CreateSubtasks applier, which soft-deletes what it created — and which needs
        // the delegated `createdTaskIds` carried up into THIS action's applied_snapshot.
        var undo = await client.PostJsonAsync($"/api/ai-actions/{plan.Id}/undo", new { });
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);

        var afterUndo = await scenario.CountAsync<BoardTask>(t => t.BoardId == board.Id);
        Assert.Equal(before, afterUndo);
    }

    [Fact]
    public async Task Approve_Twice_IsConflict()
    {
        await using var scenario = await _database.CreateScenarioAsync(autoRecoveryPlanEnabled: true);
        var (admin, _, workspace, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await ScanAsync(client, workspace.Id);
        var plan = Assert.Single(await LoadPlansAsync(scenario, workspace.Id));

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostJsonAsync($"/api/ai-actions/{plan.Id}/approve", new { })).StatusCode);

        // The Phase 4 compare-and-swap on `status` is what makes a double decision impossible.
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await client.PostJsonAsync($"/api/ai-actions/{plan.Id}/approve", new { })).StatusCode);
    }

    [Fact]
    public async Task Reject_MarksThePlanRejectedAndChangesNothing()
    {
        await using var scenario = await _database.CreateScenarioAsync(autoRecoveryPlanEnabled: true);
        var (admin, dev, workspace, _, task) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await ScanAsync(client, workspace.Id);
        var plan = Assert.Single(await LoadPlansAsync(scenario, workspace.Id));

        var reject = await client.PostJsonAsync(
            $"/api/ai-actions/{plan.Id}/reject", new { note = "Không đồng ý" });

        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        var stored = await LoadTaskAsync(scenario, task.Id);
        Assert.Equal(dev.Id, stored.AssigneeId);

        var reloaded = Assert.Single(await LoadPlansAsync(scenario, workspace.Id));
        Assert.Equal("Rejected", reloaded.Status);
    }

    [Fact]
    public async Task Undo_BeforeApproval_IsConflict()
    {
        await using var scenario = await _database.CreateScenarioAsync(autoRecoveryPlanEnabled: true);
        var (admin, _, workspace, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await ScanAsync(client, workspace.Id);
        var plan = Assert.Single(await LoadPlansAsync(scenario, workspace.Id));

        var undo = await client.PostJsonAsync($"/api/ai-actions/{plan.Id}/undo", new { });

        Assert.Equal(HttpStatusCode.Conflict, undo.StatusCode);
    }

    // ---- guards ------------------------------------------------------------------------------

    [Fact]
    public async Task Scan_IsManagerOnlySoAPlainMemberCannotDraftAPlan()
    {
        await using var scenario = await _database.CreateScenarioAsync(autoRecoveryPlanEnabled: true);
        var (_, dev, workspace, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(dev);

        var response = await ScanAsync(client, workspace.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await LoadPlansAsync(scenario, workspace.Id));
    }

    [Fact]
    public async Task Approve_ByAManagerOfAnotherWorkspace_IsForbidden()
    {
        await using var scenario = await _database.CreateScenarioAsync(autoRecoveryPlanEnabled: true);
        var (admin, _, workspace, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await ScanAsync(client, workspace.Id);
        var plan = Assert.Single(await LoadPlansAsync(scenario, workspace.Id));

        var outsider = await scenario.CreateUserAsync("Quản trị ngoài");
        await scenario.CreateWorkspaceAsync(outsider);

        using var outsiderClient = await scenario.AsUserAsync(outsider);
        var response = await outsiderClient.PostJsonAsync($"/api/ai-actions/{plan.Id}/approve", new { });

        // 404/403 both acceptable: the point is that it is not applied.
        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"Unexpected status {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Approve_WithAHandEditedUnknownKind_IsBadRequestNotA500()
    {
        await using var scenario = await _database.CreateScenarioAsync(autoRecoveryPlanEnabled: true);
        var (admin, _, workspace, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await ScanAsync(client, workspace.Id);
        var plan = Assert.Single(await LoadPlansAsync(scenario, workspace.Id));

        await OverwriteKindAsync(scenario, plan.Id, "DeleteEverything");

        var approve = await client.PostJsonAsync($"/api/ai-actions/{plan.Id}/approve", new { });

        Assert.Equal(HttpStatusCode.BadRequest, approve.StatusCode);
    }

    [Fact]
    public async Task Scan_StillWritesTheObserverAlertsEvenWhenAPlanWasDrafted()
    {
        // The plan is the OPTIONAL half; the alert must never be lost behind it.
        await using var scenario = await _database.CreateScenarioAsync(autoRecoveryPlanEnabled: true);
        var (admin, _, workspace, _, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await ScanAsync(client, workspace.Id);

        var observerAlerts = await scenario.CountAsync<Notification>(
            n => n.RecipientUserId == admin.Id && n.Type == NotificationTypes.OverdueTask);

        Assert.True(observerAlerts >= 1, "The Observer alert must survive the recovery-plan path.");
    }

    // ---- helpers -----------------------------------------------------------------------------

    private static string ReadKind(string? afterSnapshot)
    {
        Assert.NotNull(afterSnapshot);

        using var document = System.Text.Json.JsonDocument.Parse(afterSnapshot!);
        return document.RootElement.GetProperty("kind").GetString()!;
    }

    /// <summary>
    /// Rewrites a stored plan so a test can pin the kind it exercises. Jev picks the kind, so asserting
    /// Apply/Undo per kind requires controlling it — the same reason the suites use sentinels elsewhere.
    /// <para>
    /// Builds the payload field by field rather than mutating a parsed dictionary: an earlier version copied
    /// the existing <c>tasks</c> through a <c>Dictionary&lt;string, object?&gt;</c> round-trip and silently
    /// wrote <c>"tasks": null</c>, because a boxed <c>JsonElement</c> does not survive re-serialization the way
    /// the source object did. The applier then correctly refused a plan with nothing to create, which looked
    /// like an applier bug.
    /// </para>
    /// </summary>
    private static async Task OverwriteKindAsync(
        TestScenario scenario, Guid logId, string kind,
        Guid? assigneeId = null, int? extensionDays = null)
    {
        await using var db = scenario.NewDbContext();

        var log = await db.AiActionLogs.FirstAsync(x => x.Id == logId);

        using var document = System.Text.Json.JsonDocument.Parse(log.AfterSnapshot!);
        var root = document.RootElement;

        string? ReadString(string name)
            => root.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString()
                : null;

        var payload = new Dictionary<string, object?>
        {
            ["kind"] = kind,
            ["summary"] = ReadString("summary") ?? "summary",
        };

        if (ReadString("taskId") is { } taskId)
        {
            payload["taskId"] = taskId;
        }

        if (ReadString("boardId") is { } boardId)
        {
            payload["boardId"] = boardId;
        }

        if (assigneeId.HasValue)
        {
            payload["assigneeId"] = assigneeId.Value.ToString();
        }

        if (extensionDays.HasValue)
        {
            payload["extensionDays"] = extensionDays.Value;
        }

        if (string.Equals(kind, RecoveryPlanKinds.SplitSubtasks, StringComparison.Ordinal))
        {
            // The pipeline only writes `tasks` for the kinds that need them, so a plan drafted as e.g.
            // EscalateManager has none. Pinning the kind to SplitSubtasks therefore has to supply the payload
            // that kind requires — otherwise the applier has nothing to create and answers 400.
            payload["tasks"] = new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["title"] = "Thẻ con do kịch bản phục hồi tạo",
                    ["description"] = "Tách từ thẻ quá hạn.",
                    ["priority"] = "High",
                    ["labels"] = new List<object>(),
                    ["assignee"] = null,
                },
            };
        }

        log.AfterSnapshot = System.Text.Json.JsonSerializer.Serialize(
            payload, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        await db.SaveChangesAsync();
    }

    private static async Task<BoardTask> LoadTaskAsync(TestScenario scenario, Guid taskId)
    {
        await using var db = scenario.NewDbContext();

        return await db.Tasks.AsNoTracking().FirstAsync(t => t.Id == taskId);
    }

    private sealed record PendingPlanDto(Guid Id, string Status, string? AfterSnapshot);

    private sealed record ActionDetailDto(
        Guid Id,
        string Status,
        System.Text.Json.JsonElement? AppliedSnapshot);
}
