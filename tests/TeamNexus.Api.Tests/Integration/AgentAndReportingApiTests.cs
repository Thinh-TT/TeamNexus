using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Services.Agent;
using TeamNexus.Modules.Auth;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 8 §2.6 — the AI Agent Executor and the reporting exporter, both fully offline.
/// <para>
/// The agent runs in a background scope (202 Accepted), so these suites poll for the terminal state
/// with a bounded timeout. That is the honest way to test it: asserting on the 202 alone would never
/// exercise the tool loop, the guardrails or the appliers.
/// </para>
/// <para>
/// <b>Known coverage boundary — recorded rather than faked:</b> the offline fake's scripts always
/// converge on <c>DraftOutput</c> or <c>RequestClarification</c>, so a run that overflows
/// <c>Agent:MaxToolCalls</c> cannot be produced through the API without a provider that loops
/// forever. <c>ToolLimit</c> (and the other three thresholds) is therefore covered exactly at the
/// decision boundary by <c>Pure.AgentGuardrailsTests</c>, which is the only place the rule lives; the
/// integration side covers the <b>wall-clock</b> guardrail, which the fake <i>can</i> trigger via its
/// <c>FAKE:SLOW</c> sentinel.
/// </para>
/// </summary>
public sealed class AgentAndReportingApiTests : IClassFixture<DatabaseFixture>
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);

    private readonly DatabaseFixture _database;

    public AgentAndReportingApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- agent identity & assignment ---------------------------------------

    [Fact]
    public async Task AgentMember_IsCreatedLazilyAndIsAssignable()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var agentId = await GetAgentUserIdAsync(client, seed.Workspace.Id);
        var task = await scenario.CreateTaskAsync(seed.Board, seed.Todo, seed.User, "Task cho agent");

        var assign = await client.PutJsonAsync(
            $"/api/boards/{seed.Board.Id}/tasks/{task.Id}",
            new { title = "Task cho agent", assigneeId = agentId });

        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);

        var updated = await assign.Content.ReadFromJsonAsync<AgentTaskResponse>();
        Assert.Equal(agentId, updated!.AssigneeId);
        Assert.True(updated.AssigneeIsAiAgent);
    }

    // ---- happy path: draft → Pending approval -------------------------------

    [Fact]
    public async Task AgentRun_ProducesADraftThatWaitsForHumanApproval()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var task = await AssignToAgentAsync(client, scenario, seed, "Soạn mô tả tính năng");

        var run = await StartRunAsync(client, task.Id);
        Assert.Equal("Running", run.Status);

        var finished = await WaitForTerminalRunAsync(client, run.Id);

        // The agent stops in a state that NEEDS a human: nothing is applied without approval.
        Assert.Equal("AwaitingApproval", finished.Status);
        Assert.Equal("DraftProduced", finished.StopReason);
        Assert.Equal("Comment", finished.OutputKind);
        Assert.NotNull(finished.AiActionLogId);
        Assert.True(finished.ToolCallCount >= 3, $"Expected >= 3 tool calls, got {finished.ToolCallCount}.");

        // The tool trace is populated and readable (the Manager's evidence of what happened).
        var detail = await client.GetJsonAsync<AgentRunDetailResponse>($"/api/agent-runs/{run.Id}");
        Assert.NotNull(detail);
        Assert.NotEmpty(detail!.ToolCallTrace);
        Assert.Equal(["SearchSystemData", "WebSearch", "DraftOutput"], detail.ToolCallTrace.Select(t => t.Name));

        // …and the produced draft sits in the Accountability Layer as Pending.
        var log = await client.GetJsonAsync<AiActionLogDetailResponse>($"/api/ai-actions/{finished.AiActionLogId}");
        Assert.NotNull(log);
        Assert.Equal("Pending", log!.Status);

        // No comment yet: a draft is a proposal, not a post.
        var comments = await client.GetJsonAsync<List<CommentRow>>($"/api/tasks/{task.Id}/comments");
        Assert.NotNull(comments);
        Assert.Empty(comments!);
    }

    // ---- append-only rerun --------------------------------------------------

    [Fact]
    public async Task AgentRun_AppendOnly_RerunAfterAnAnswerCreatesANewRowAndLeavesTheOldOneUntouched()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        // The documented rerun flow (Phase 7 D2/D14): the agent asks a question, the team lead
        // answers with a comment, and "Chạy lại" consumes that answer — the agent never wakes up on
        // its own when a comment arrives.
        var task = await AssignToAgentAsync(
            client, scenario, seed, $"{AgentMarkers.FakeSentinelPrefix}CLARIFY cần làm rõ để chạy lại");

        var first = await StartRunAsync(client, task.Id);
        var firstFinished = await WaitForTerminalRunAsync(client, first.Id);
        Assert.Equal("AwaitingClarification", firstFinished.Status);

        var firstRowBefore = await scenario.FindAsync<AgentRun>(first.Id);
        Assert.NotNull(firstRowBefore);

        // The human answer must be newer than the run's finish — that is what the orchestrator looks for.
        var answer = await client.PostJsonAsync(
            $"/api/tasks/{task.Id}/comments",
            new { content = "Đây là câu trả lời của trưởng nhóm." });
        Assert.Equal(HttpStatusCode.Created, answer.StatusCode);

        var rerunResponse = await client.PostJsonAsync(
            $"/api/tasks/{task.Id}/agent-runs/{first.Id}/rerun",
            new { });

        Assert.Equal(HttpStatusCode.Accepted, rerunResponse.StatusCode);

        var second = await rerunResponse.Content.ReadFromJsonAsync<AgentRunResponse>();
        Assert.NotNull(second);
        Assert.NotEqual(first.Id, second!.Id);

        await WaitForTerminalRunAsync(client, second.Id);

        // The new run points back at the one it replaces, carrying the question it inherited.
        var detail = await client.GetJsonAsync<AgentRunDetailResponse>($"/api/agent-runs/{second.Id}");
        Assert.Equal(first.Id, detail!.PreviousRunId);
        Assert.Equal(firstFinished.ClarificationQuestion, detail.PreviousQuestion);
        Assert.Equal("Đây là câu trả lời của trưởng nhóm.", detail.ResolutionCommentContent);

        // …and the old row is unchanged: runs are an append-only audit journal (Phase 7 D14).
        var firstRowAfter = await scenario.FindAsync<AgentRun>(first.Id);
        Assert.NotNull(firstRowAfter);
        Assert.Equal(firstRowBefore!.Status, firstRowAfter!.Status);
        Assert.Equal(firstRowBefore.StopReason, firstRowAfter.StopReason);
        Assert.Equal(firstRowBefore.FinishedAt, firstRowAfter.FinishedAt);
        Assert.Equal(firstRowBefore.ToolCallCount, firstRowAfter.ToolCallCount);

        // Two distinct runs exist for the task.
        var history = await client.GetJsonAsync<List<AgentRunResponse>>($"/api/tasks/{task.Id}/agent-runs");
        Assert.Equal(2, history!.Count);
    }

    [Fact]
    public async Task Rerun_ForARunThatDidNotAskAQuestion_Returns400()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        // The default script produces a draft (AwaitingApproval), which is NOT a clarification stop.
        var task = await AssignToAgentAsync(client, scenario, seed, "Chạy xong rồi chạy lại sai cách");

        var run = await StartRunAsync(client, task.Id);
        await WaitForTerminalRunAsync(client, run.Id);

        var rerun = await client.PostJsonAsync($"/api/tasks/{task.Id}/agent-runs/{run.Id}/rerun", new { });

        Assert.Equal(HttpStatusCode.BadRequest, rerun.StatusCode);
    }

    // ---- guardrails ---------------------------------------------------------

    [Fact]
    public async Task AgentRun_WithEveryTurnStalled_StopsOnTheWallClockBudget()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        // The FAKE:SLOW sentinel makes every provider turn stall for seconds, so a handful of turns
        // already exceed a 5-second wall-clock budget: the guardrail, not the model, ends this run.
        using var strictFactory = new TeamNexusApiFactory
        {
            ConnectionString = TestScenario.ConnectionStringForTests,
            AgentRunTimeoutSeconds = 5,
        };

        using var strictClient = await AuthenticatedClientWithCsrfAsync(strictFactory, seed.User);

        var task = await AssignToAgentAsync(
            client, scenario, seed, $"{AgentMarkers.FakeSentinelPrefix}SLOW chậm có chủ đích");

        var start = await strictClient.PostAsync($"/api/tasks/{task.Id}/agent-runs", content: null);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);

        var started = await start.Content.ReadFromJsonAsync<AgentRunResponse>();
        Assert.NotNull(started);

        var finished = await WaitForTerminalRunAsync(client, started!.Id, attempts: 180);

        Assert.True(
            finished.Status == "Failed",
            $"Expected a Failed run, got status={finished.Status} stopReason={finished.StopReason} "
            + $"tools={finished.ToolCallCount} tokens={finished.TotalTokens} error={finished.Error}.");

        Assert.Equal("TimeLimit", finished.StopReason);
        Assert.False(string.IsNullOrWhiteSpace(finished.Error));

        // "Báo Manager đúng việc cần làm": the message names the threshold and the actual figure.
        Assert.Contains("RunTimeoutSeconds", finished.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentRun_WhenTheModelCallsAToolOutsideTheWhitelist_TheRunSurvivesAndTheTraceRecordsTheError()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var task = await AssignToAgentAsync(
            client, scenario, seed, $"{AgentMarkers.FakeSentinelPrefix}UNKNOWN gọi tool lạ");

        var run = await StartRunAsync(client, task.Id);
        var finished = await WaitForTerminalRunAsync(client, run.Id);

        // The whitelist is enforced by returning an error RESULT to the model, not by aborting the
        // run: a hallucinated tool must not waste the whole run (Phase 7 §4.3).
        Assert.Equal("AwaitingApproval", finished.Status);
        Assert.Equal("DraftProduced", finished.StopReason);

        var detail = await client.GetJsonAsync<AgentRunDetailResponse>($"/api/agent-runs/{run.Id}");
        Assert.NotNull(detail);

        var rejected = Assert.Single(detail!.ToolCallTrace, t => t.IsError);
        Assert.Equal("BogusTool", rejected.Name);
    }

    [Fact]
    public async Task AgentRun_CancelStopsTheRunAndRecordsCancelled()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        // FAKE:SLOW stalls a provider turn, giving the test a window to cancel.
        var task = await AssignToAgentAsync(
            client, scenario, seed, $"{AgentMarkers.FakeSentinelPrefix}SLOW chạy chậm để huỷ");

        var run = await StartRunAsync(client, task.Id);

        var cancel = await client.PostJsonAsync($"/api/agent-runs/{run.Id}/cancel", new { });

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        // The cancel route answers with the run DETAIL wrapper, like every other run read.
        var cancelled = await cancel.Content.ReadFromJsonAsync<AgentRunDetailResponse>();
        Assert.NotNull(cancelled);
        Assert.Equal("Cancelled", cancelled!.Run.StopReason);
        Assert.Equal("Failed", cancelled.Run.Status);
    }

    [Fact]
    public async Task AgentEndpoints_WhenTheFeatureIsDisabled_Return503()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        var task = await scenario.CreateTaskAsync(seed.Board, seed.Todo, seed.User, "Task khi agent tắt");

        // A dedicated host with Agent:Enabled=false, so the production off-switch is exercised
        // without disturbing the shared host every other suite uses.
        using var disabledFactory = new TeamNexusApiFactory
        {
            ConnectionString = TestScenario.ConnectionStringForTests,
            AgentEnabled = false,
        };

        using var client = await AuthenticatedClientWithCsrfAsync(disabledFactory, seed.User);

        var response = await client.PostAsync($"/api/tasks/{task.Id}/agent-runs", content: null);

        // 503 (feature off), not 403: the seed user is a workspace Admin, so the permission gate
        // passes and the feature gate is what answers.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task AgentRunReaper_ClosesAnOrphanRunLeftBehindByASleepingHost()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User);

        var agentId = await GetAgentUserIdAsync(client, seed.Workspace.Id);
        var task = await scenario.CreateTaskAsync(seed.Board, seed.Todo, seed.User, "Run mồ côi", assigneeId: agentId);

        // A run the free-tier host "forgot" when it spun down: still Running, started long ago
        // (past RunTimeoutSeconds + OrphanRunGraceSeconds).
        var orphanId = Guid.NewGuid();

        await scenario.WithDbContextAsync(async db =>
        {
            db.AgentRuns.Add(new AgentRun
            {
                Id = orphanId,
                WorkspaceId = seed.Workspace.Id,
                BoardId = seed.Board.Id,
                TaskId = task.Id,
                AgentUserId = agentId,
                TriggeredByUserId = seed.User.Id,
                Status = AgentRunStatus.Running,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            });

            await db.SaveChangesAsync();
        });

        // Restarting a host is what runs the reaper (IHostedService) — exactly the free-tier scenario
        // the feature exists for.
        using (var restarted = new TeamNexusApiFactory { ConnectionString = TestScenario.ConnectionStringForTests })
        {
            _ = restarted.Services;

            await WaitUntilAsync(
                async () =>
                {
                    var row = await scenario.FindAsync<AgentRun>(orphanId);
                    return row?.Status == AgentRunStatus.Failed;
                },
                "the reaper to close the orphan run");
        }

        var reaped = await scenario.FindAsync<AgentRun>(orphanId);
        Assert.NotNull(reaped);
        Assert.Equal(AgentRunStatus.Failed, reaped!.Status);
        Assert.Equal(AgentStopReason.InternalError, reaped.StopReason);
        Assert.NotNull(reaped.FinishedAt);
    }

    // ---- reporting ----------------------------------------------------------

    [Fact]
    public async Task ReportSummary_ReturnsProgressMetricsForTheWorkspace()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User, "Admin");

        // One open task and one genuinely completed task, so the metrics have something to count.
        await scenario.CreateTaskAsync(seed.Board, seed.Todo, seed.User, "Đang làm");
        var finishedTask = await scenario.CreateTaskAsync(seed.Board, seed.Done, seed.User, "Đã xong");

        await scenario.WithDbContextAsync(async db =>
        {
            var row = await db.Tasks.FindAsync(finishedTask.Id);
            row!.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        });

        var summary = await client.GetJsonAsync<ReportSummaryResponse>(
            $"/api/workspaces/{seed.Workspace.Id}/reports/summary");

        Assert.NotNull(summary);
        Assert.Equal(seed.Workspace.Id, summary!.WorkspaceId);
        Assert.Equal(2, summary.Progress.Total);
        Assert.Equal(1, summary.Progress.Done);
    }

    [Fact]
    public async Task ReportExport_ReturnsAPdfAndAnExcelThatAreReallyThoseFormats()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User, "Admin");

        await scenario.CreateTaskAsync(seed.Board, seed.Todo, seed.User, "Task để xuất báo cáo");

        var pdf = await client.GetAsync($"/api/workspaces/{seed.Workspace.Id}/reports/export?format=pdf");
        var excel = await client.GetAsync($"/api/workspaces/{seed.Workspace.Id}/reports/export?format=excel");

        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal(HttpStatusCode.OK, excel.StatusCode);

        var pdfBytes = await pdf.Content.ReadAsByteArrayAsync();
        var excelBytes = await excel.Content.ReadAsByteArrayAsync();

        // Magic numbers, not just "some bytes": %PDF- for PDF, PK zip for xlsx.
        Assert.True(pdfBytes.Length > 5);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdfBytes, 0, 5));
        Assert.Contains(
            "pdf",
            pdf.Content.Headers.ContentType!.MediaType!,
            StringComparison.OrdinalIgnoreCase);

        Assert.True(excelBytes.Length > 2);
        Assert.Equal([0x50, 0x4B], excelBytes[..2]);

        // The file name is ASCII-only, because it travels in Content-Disposition (Phase 6 §4.2).
        var fileName = pdf.Content.Headers.ContentDisposition?.FileNameStar
                       ?? pdf.Content.Headers.ContentDisposition?.FileName
                       ?? string.Empty;

        Assert.Contains("teamnexus-report", fileName, StringComparison.Ordinal);
        Assert.All(
            fileName,
            ch => Assert.True(ch < 128, $"Non-ASCII character '{ch}' in file name '{fileName}'."));
    }

    [Fact]
    public async Task ReportExport_WithAnUnknownFormat_Returns400()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario);
        using var client = await scenario.AsUserAsync(seed.User, "Admin");

        var response = await client.GetAsync(
            $"/api/workspaces/{seed.Workspace.Id}/reports/export?format=docx");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reports_ForAWorkspaceMemberWithoutTheManagerRole_AreForbidden()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var seed = await SeedAsync(scenario, role: WorkspaceRole.Member);
        using var client = await scenario.AsUserAsync(seed.User, "Member");

        var summary = await client.GetAsync($"/api/workspaces/{seed.Workspace.Id}/reports/summary");

        Assert.Equal(HttpStatusCode.Forbidden, summary.StatusCode);
    }

    // ---- helpers ------------------------------------------------------------

    private sealed record Seed(
        ApplicationUser User, Workspace Workspace, Board Board, BoardColumn Todo, BoardColumn Done);

    private static async Task<Seed> SeedAsync(TestScenario scenario, WorkspaceRole role = WorkspaceRole.Admin)
    {
        var user = await scenario.CreateUserAsync("Trưởng nhóm");
        var workspace = await scenario.CreateWorkspaceAsync(user, "Workspace Agent");

        if (role != WorkspaceRole.Admin)
        {
            // The seeded owner starts as Admin; downgrade so the role gate can actually be exercised.
            await scenario.WithDbContextAsync(async db =>
            {
                var membership = await db.WorkspaceMembers.FindAsync(workspace.Id, user.Id);
                membership!.Role = role;
                await db.SaveChangesAsync();
            });
        }

        var (board, todo, done) = await scenario.CreateBoardAsync(workspace);
        return new Seed(user, workspace, board, todo, done);
    }

    /// <summary>
    /// Builds a client for a private host (one with a non-default feature switch) that carries a
    /// working antiforgery pair — without it every POST/PUT/DELETE answers 403 and hides the
    /// behaviour the test is actually about. The jar is per-client, so this host's session never
    /// leaks into the suite's shared one.
    /// </summary>
    private static async Task<HttpClient> AuthenticatedClientWithCsrfAsync(
        TeamNexusApiFactory factory, ApplicationUser user)
    {
        var jar = new System.Net.CookieContainer();

        var client = factory.CreateDefaultClient(new SessionCookieHandler(jar));
        client.BaseAddress = TestScenario.BaseAddress;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.Create(user, "Admin"));

        var tokenResponse = await client.GetAsync("/api/auth/antiforgery");
        tokenResponse.EnsureSuccessStatusCode();

        var token = ReadXsrfToken(tokenResponse)
            ?? throw new InvalidOperationException("No XSRF-TOKEN cookie from /api/auth/antiforgery.");

        client.DefaultRequestHeaders.Add(AuthConstants.XsrfRequestHeader, token);
        return client;
    }

    private static string? ReadXsrfToken(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
            return null;
        }

        foreach (var header in headers)
        {
            var first = header.Split(';')[0];
            var equals = first.IndexOf('=');

            if (equals > 0 && first[..equals].Trim() == AuthConstants.XsrfTokenCookie)
            {
                return Uri.UnescapeDataString(first[(equals + 1)..]);
            }
        }

        return null;
    }

    /// <summary>Fetches the lazily-created AI Agent member of the workspace.</summary>
    private static async Task<Guid> GetAgentUserIdAsync(TestHttpClient client, Guid workspaceId)
    {
        var members = await client.GetJsonAsync<List<MemberRow>>($"/api/workspaces/{workspaceId}/members");
        Assert.NotNull(members);

        var agent = members!.SingleOrDefault(m => m.MemberType == "ai_agent");
        Assert.NotNull(agent);

        return agent!.UserId;
    }

    private static async Task<AgentTaskResponse> AssignToAgentAsync(
        TestHttpClient client, TestScenario scenario, Seed seed, string title)
    {
        var agentId = await GetAgentUserIdAsync(client, seed.Workspace.Id);
        var task = await scenario.CreateTaskAsync(seed.Board, seed.Todo, seed.User, title, assigneeId: agentId);

        return new AgentTaskResponse(task.Id, task.ColumnId, task.Title, agentId, true);
    }

    private static async Task<AgentRunResponse> StartRunAsync(TestHttpClient client, Guid taskId)
    {
        var response = await client.PostJsonAsync($"/api/tasks/{taskId}/agent-runs", new { });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var run = await response.Content.ReadFromJsonAsync<AgentRunResponse>();
        Assert.NotNull(run);
        return run!;
    }

    /// <summary>
    /// Polls a run until it stops being <c>Running</c>, or fails the test after a bounded wait.
    /// <c>AwaitingApproval</c> / <c>AwaitingClarification</c> count as terminal for a run: the agent
    /// has done its part and the ball is back with a human.
    /// </summary>
    private static async Task<AgentRunResponse> WaitForTerminalRunAsync(
        TestHttpClient client, Guid runId, int attempts = 60)
    {
        AgentRunDetailResponse? detail = null;

        await WaitUntilAsync(
            async () =>
            {
                detail = await client.GetJsonAsync<AgentRunDetailResponse>($"/api/agent-runs/{runId}");
                return detail?.Run is { Status: not "Running" };
            },
            $"run {runId} to leave the Running state",
            attempts);

        return detail!.Run;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string what, int attempts = 90)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException($"Timed out waiting for {what} ({attempts} polls).");
    }

    // ---- response shapes mirrored from the module DTOs ----------------------

    private sealed record AgentRunResponse(
        Guid Id,
        Guid TaskId,
        Guid BoardId,
        string Status,
        string? StopReason,
        string? ClarificationQuestion,
        Guid? AiActionLogId,
        string? OutputKind,
        string? Error,
        int ToolCallCount,
        int LlmCallCount,
        int TotalTokens,
        DateTimeOffset StartedAt,
        DateTimeOffset? FinishedAt);

    private sealed record AgentRunDetailResponse(
        AgentRunResponse Run,
        IReadOnlyList<TraceRow> ToolCallTrace,
        Guid? PreviousRunId,
        string? PreviousQuestion,
        string? ResolutionCommentContent);

    private sealed record TraceRow(string Name, string? Arguments, string? ResultSummary, bool IsError, int DurationMs);

    private sealed record AgentTaskResponse(
        Guid Id, Guid ColumnId, string Title, Guid? AssigneeId, bool AssigneeIsAiAgent);

    private sealed record MemberRow(Guid UserId, string DisplayName, string Role, string MemberType);

    private sealed record CommentRow(Guid Id, Guid TaskId, string Content);

    private sealed record ColumnRow(Guid Id, string Name, int Position, bool IsDone, bool IsClarification);

    private sealed record ProgressRow(int Total, int Done, int Open, int Overdue, double DonePercent);

    private sealed record ReportSummaryResponse(Guid WorkspaceId, ProgressRow Progress);
}
