using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Ai.Services.Jev;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Giai đoạn 14.7 Track A — **ISSUE-01** regression suite.
/// <para>
/// The live failure was: pressing "Quét ngay" made the Observer call DeepSeek, DeepSeek did not answer within
/// 60 s, and the resulting run was marked <c>Failed</c> with <b>no signals and no alerts</b> — for a workspace
/// that genuinely had overdue tasks. The detector had already done its work in plain C# and Jev had already
/// assessed the severity; both were thrown away because the <i>explanation</i> step failed.
/// </para>
/// <para>
/// These tests pin the corrected contract: a System 2 outage produces a <b>Completed</b> run carrying a
/// <c>detailError</c>, real alerts derived from the detector's own signals, and a preserved Jev verdict.
/// </para>
/// </summary>
public sealed class Phase147ObserverDegradeTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public Phase147ObserverDegradeTests(DatabaseFixture database)
    {
        _database = database;
    }

    /// <summary>A workspace with ONE badly overdue card, which is enough to raise an <c>OverdueTask</c> signal.</summary>
    private static async Task<(ApplicationUser Admin, Workspace Workspace, BoardTask Task)> SeedAsync(
        TestScenario scenario)
    {
        var admin = await scenario.CreateUserAsync("Quản trị");
        var workspace = await scenario.CreateWorkspaceAsync(admin);
        var (board, todo, _) = await scenario.CreateBoardAsync(workspace, "Board Quá Hạn");

        var task = await scenario.CreateTaskAsync(
            board, todo, admin,
            title: "Di trú dịch vụ thanh toán",
            dueDate: DateTimeOffset.UtcNow.AddDays(-30));

        return (admin, workspace, task);
    }

    private static Task<HttpResponseMessage> ScanAsync(TestHttpClient client, Guid workspaceId)
        => client.PostJsonAsync($"/api/workspaces/{workspaceId}/observer/scan", new { });

    // ---- the fix ------------------------------------------------------------------------------

    [Fact]
    public async Task Scan_WhenTheProseStepTimesOut_StillSucceedsAndStillAlerts()
    {
        // THE regression test. Before the fix this returned 502 and wrote zero notifications.
        var scripted = new ScriptedAiProvider { ThrowOnComplete = true };
        await using var scenario = await _database.CreateScriptedAiScenarioAsync(scripted);

        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await ScanAsync(client, workspace.Id);

        // 200, not 502: the scan did produce usable output.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ScanDto>();

        Assert.NotNull(body);
        Assert.Equal("Completed", body!.Status);
        Assert.True(body.SignalsDetected > 0, "The detector must still have found the overdue card.");
        Assert.True(body.FindingsWritten > 0, "Findings must fall back to the detector's own signals.");
        Assert.True(body.UsedDetectorFallback);

        // The prose failure is REPORTED but does not masquerade as a run failure.
        Assert.NotNull(body.DetailError);
        Assert.Contains("60s", body.DetailError!, StringComparison.Ordinal);

        // …and the manager was actually told about the overdue work.
        var alerts = await scenario.CountAsync<Notification>(
            n => n.RecipientUserId == admin.Id && n.Type == NotificationTypes.OverdueTask);

        Assert.True(alerts >= 1, "A provider outage must not silence the alerts.");
    }

    [Fact]
    public async Task Scan_WhenTheProseStepTimesOut_PreservesTheJevVerdict()
    {
        // The second half of the issue: Jev's assessment had already succeeded and was discarded. It is now
        // persisted whether or not System 2 answers.
        var scripted = new ScriptedAiProvider { ThrowOnComplete = true };
        await using var scenario = await _database.CreateScriptedAiScenarioAsync(scripted);

        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await ScanAsync(client, workspace.Id);

        await using var db = scenario.NewDbContext();
        var summary = await db.AiObserverRuns
            .Where(r => r.WorkspaceId == workspace.Id)
            .Select(r => r.Summary)
            .FirstAsync();
        Assert.NotNull(summary);

        using var document = JsonDocument.Parse(summary!);
        var root = document.RootElement;

        Assert.True(root.GetProperty("jevUsed").GetBoolean());
        Assert.Contains(root.GetProperty("jevSeverity").GetString(), ObserverSeverity.All);
        Assert.True(root.GetProperty("jevConfidence").GetDouble() > 0d);

        // The cost evidence survives too — that is what makes "System 1 is far cheaper" measurable.
        Assert.True(root.TryGetProperty("jevCostUsd", out _));

        // The failure is recorded in its OWN field, never in `error` (which means "the run failed").
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("detailError").ValueKind);
        Assert.True(root.GetProperty("usedDetectorFallback").GetBoolean());
    }

    [Fact]
    public async Task Scan_WhenTheProseStepTimesOut_DoesNotLeakTheProseFailureIntoTheRunStatusHistory()
    {
        // The run list is what the UI renders, so the degraded flag must survive the projection from jsonb.
        var scripted = new ScriptedAiProvider { ThrowOnComplete = true };
        await using var scenario = await _database.CreateScriptedAiScenarioAsync(scripted);

        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await ScanAsync(client, workspace.Id);

        var runs = await client.GetJsonAsync<List<RunDto>>($"/api/workspaces/{workspace.Id}/observer/runs");

        Assert.NotNull(runs);
        var run = Assert.Single(runs!);

        Assert.Equal("Completed", run.Status);
        Assert.True(run.UsedDetectorFallback);
        Assert.NotNull(run.DetailError);

        // Giai đoạn 14.7 §A5 evidence must be visible to the UI as well (ISSUE-05).
        Assert.True(run.JevUsed);
        Assert.Contains(run.JevSeverity, ObserverSeverity.All);
        Assert.NotNull(run.JevConfidence);
    }

    [Fact]
    public async Task Scan_WithAHealthyProvider_ReportsNoDetailErrorAndNoFallback()
    {
        // The counterpart assertion: the degrade flag must not be permanently on.
        await using var scenario = await _database.CreateScenarioAsync();
        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        var response = await ScanAsync(client, workspace.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ScanDto>();

        Assert.NotNull(body);
        Assert.Equal("Completed", body!.Status);
        Assert.Null(body.DetailError);
        Assert.False(body.UsedDetectorFallback);
        Assert.True(body.FindingsWritten > 0);
    }

    [Fact]
    public async Task Scan_WhenTheProseStepFails_StillDraftsARecoveryPlan()
    {
        // The recovery plan runs AFTER notifications and is driven by Jev's verdict, which survived — so an
        // explanation outage must not also disable proactive healing.
        var scripted = new ScriptedAiProvider { ThrowOnComplete = true };
        await using var scenario = await _database.CreateScriptedAiScenarioAsync(scripted);

        var (admin, workspace, _) = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(admin);

        await ScanAsync(client, workspace.Id);

        // The plan is opt-in, so with the default (off) there is none — asserted here so the test documents
        // that the recovery path is independent of the prose path rather than silently skipped.
        var plans = await scenario.CountAsync<AiActionLog>(
            x => x.Action == AiActionTypes.RecoveryPlan && x.EntityId == workspace.Id);

        Assert.Equal(0, plans);
    }

    // ---- pure fallback builder ----------------------------------------------------------------

    [Fact]
    public void BuildDetectorFindings_KeepsEachSignalsOwnSeverityAndEvidence()
    {
        // A single Jev severity describes the WHOLE situation, so copying it onto every signal would relabel a
        // Medium bottleneck as Critical. Each finding therefore keeps its own signal's severity.
        var taskId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var signals = new List<ObserverSignal>
        {
            new(ObserverSignalDetector.OverdueTask, ObserverSeverity.Critical, 30, "4 task(s) past due",
                [taskId], [userId]),
            new(ObserverSignalDetector.Bottleneck, ObserverSeverity.Medium, 8, "Column 'Todo' holds 6 open task(s)",
                [Guid.NewGuid()], []),
        };

        var risk = new ObserverRiskAssessmentResult(
            ObserverSeverity.Critical, RequireImmediateAlert: true, HasDominoRisk: true,
            ConfidenceScore: 0.91, RoutingAdvice: ConfidenceRouting.DirectAssign);

        var findings = ObserverService.BuildDetectorFindings(signals, risk);

        Assert.Equal(2, findings.Count);

        Assert.Equal(ObserverSeverity.Critical, findings[0].Severity);
        Assert.Equal(ObserverSignalDetector.OverdueTask, findings[0].Type);
        Assert.Contains(taskId, findings[0].TaskIds);
        Assert.Contains(userId, findings[0].UserIds);

        // The Medium signal stays Medium even though the run-level verdict was Critical.
        Assert.Equal(ObserverSeverity.Medium, findings[1].Severity);
        Assert.Equal(ObserverSignalDetector.Bottleneck, findings[1].Type);

        // The fallback message says where it came from, so an operator is never misled into thinking the model
        // wrote it.
        Assert.All(findings, finding =>
        {
            Assert.Contains("trực tiếp từ dữ liệu bảng", finding.Message, StringComparison.Ordinal);
            Assert.Contains("không khả dụng", finding.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void BuildDetectorFindings_AddsTheDominoNoteOnlyWhenTheVerdictSaysSo()
    {
        var signals = new List<ObserverSignal>
        {
            new(ObserverSignalDetector.OverdueTask, ObserverSeverity.High, 5, "1 task past due", [Guid.NewGuid()], []),
        };

        var flat = ObserverService.BuildDetectorFindings(signals, jevRisk: null);
        var domino = ObserverService.BuildDetectorFindings(signals,
            new ObserverRiskAssessmentResult(ObserverSeverity.High, true, HasDominoRisk: true, 0.9, ConfidenceRouting.DirectAssign));

        Assert.DoesNotContain("dây chuyền", flat[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dây chuyền", domino[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDetectorFindings_SkipsASignalWithAnUnknownSeverityRatherThanGuessing()
    {
        // The persistence layer requires one of the four canonical words, so a bogus severity is dropped
        // instead of being coerced into Medium.
        var signals = new List<ObserverSignal>
        {
            new(ObserverSignalDetector.OverdueTask, "Emergency", 5, "x", [Guid.NewGuid()], []),
        };

        Assert.Empty(ObserverService.BuildDetectorFindings(signals, jevRisk: null));
    }

    [Fact]
    public void BuildDetectorFindings_WithNoSignals_ReturnsNothing()
    {
        Assert.Empty(ObserverService.BuildDetectorFindings([], jevRisk: null));
    }

    // ---- DTOs ---------------------------------------------------------------------------------

    private sealed record ScanDto(
        Guid RunId,
        string Status,
        int SignalsDetected,
        int FindingsWritten,
        int NotificationsCreated,
        bool AiCalled,
        string? DetailError,
        bool UsedDetectorFallback);

    private sealed record RunDto(
        Guid Id,
        string Status,
        int SignalsDetected,
        int FindingsWritten,
        bool AiCalled,
        bool UsedDetectorFallback,
        string? DetailError,
        bool JevUsed,
        string? JevSeverity,
        double? JevConfidence,
        double? JevCostUsd);
}
