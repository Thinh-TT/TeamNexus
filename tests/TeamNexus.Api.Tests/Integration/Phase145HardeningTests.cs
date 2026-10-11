using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Ai.Services.Agent;
using TeamNexus.Modules.Ai.Services.Integrations;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 14.5 §Track A — three hardening items verified together because each one is a
/// <b>lifecycle/seam</b> fix rather than a feature (PHASE145-A1…A5).
/// <list type="bullet">
///   <item><b>A2.1</b> — the run reaper also closes runs whose task was soft-deleted, not just ones that
///   are old (an unfinished run used to keep a hidden card's slot for up to ten minutes).</item>
///   <item><b>A2.4</b> — the chat retention runner deletes only what is past the window, in batches.</item>
///   <item><b>A1.2</b> — every notification family reaches the outbound alert seam, which is what lets
///   Phase 15 add an external transport without editing the Observer, the agent or the member path.</item>
/// </list>
/// </summary>
public sealed class Phase145HardeningTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public Phase145HardeningTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- A2.1: reaper closes runs of soft-deleted tasks --------------------

    [Fact]
    public async Task PHASE145_A1_TheReaperClosesARunWhoseTaskWasDeleted()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        var user = await scenario.CreateUserAsync("Chủ workspace");
        var workspace = await scenario.CreateWorkspaceAsync(user, "Workspace reaper");
        var (board, todo, _) = await scenario.CreateBoardAsync(workspace);

        var agentId = Guid.NewGuid();

        await scenario.WithDbContextAsync(async db =>
        {
            db.Users.Add(new ApplicationUser
            {
                Id = agentId,
                UserName = $"agent-{agentId:N}@example.test",
                NormalizedUserName = $"AGENT-{agentId:N}@EXAMPLE.TEST",
                Email = null,
                DisplayName = "TeamNexus Agent",
                LockoutEnabled = true,
                TwoFactorEnabled = true,
            });

            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspace.Id,
                UserId = agentId,
                Role = WorkspaceRole.Member,
                MemberType = MemberType.AiAgent,
                AiAgentName = "TeamNexus Agent",
                JoinedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync();
        });

        var task = await scenario.CreateTaskAsync(board, todo, user, "Thẻ sẽ bị xoá", assigneeId: agentId);

        var runId = Guid.NewGuid();

        await scenario.WithDbContextAsync(async db =>
        {
            db.AgentRuns.Add(new AgentRun
            {
                Id = runId,
                WorkspaceId = workspace.Id,
                BoardId = board.Id,
                TaskId = task.Id,
                AgentUserId = agentId,
                TriggeredByUserId = user.Id,
                Status = AgentRunStatus.AwaitingApproval,
                // FRESH on purpose: the age sweep must not be what closes it, otherwise this test would
                // pass without the new rule and prove nothing.
                StartedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync();
        });

        // Soft-delete the task through the Board service, which is what a user does.
        using var client = await scenario.AsUserAsync(user, "Manager");
        var delete = await client.DeleteAsync($"/api/boards/{board.Id}/tasks/{task.Id}");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, delete.StatusCode);

        // The runner is directly callable precisely so this does not have to wait for a hosted service.
        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<AgentRunReaperRunner>();
            await runner.RunOnceAsync();
        }

        var closed = await scenario.FindIncludingSoftDeletedAsync<AgentRun>(runId);
        Assert.NotNull(closed);
        Assert.Equal(AgentRunStatus.Failed, closed!.Status);

        // `TaskChanged` mirrors exactly what a LIVE orchestrator records when the task moves out from under
        // it — and, like there, no notification is written: deleting a card is already a deliberate act.
        Assert.Equal(AgentStopReason.TaskChanged, closed.StopReason);
        Assert.NotNull(closed.FinishedAt);
    }

    [Fact]
    public async Task PHASE145_A2_TheReaperLeavesRunsOfLiveTasksAlone()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        var user = await scenario.CreateUserAsync("Chủ workspace");
        var workspace = await scenario.CreateWorkspaceAsync(user, "Workspace reaper 2");
        var (board, todo, _) = await scenario.CreateBoardAsync(workspace);
        var task = await scenario.CreateTaskAsync(board, todo, user, "Thẻ còn sống");

        var runId = Guid.NewGuid();

        await scenario.WithDbContextAsync(async db =>
        {
            db.AgentRuns.Add(new AgentRun
            {
                Id = runId,
                WorkspaceId = workspace.Id,
                BoardId = board.Id,
                TaskId = task.Id,
                AgentUserId = user.Id,
                TriggeredByUserId = user.Id,
                Status = AgentRunStatus.Running,
                StartedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync();
        });

        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<AgentRunReaperRunner>();
            await runner.RunOnceAsync();
        }

        // A fresh run on a live task is exactly what the sweep must NOT touch: closing it would abort every
        // legitimate in-flight run on every host restart.
        var run = await scenario.FindAsync<AgentRun>(runId);
        Assert.NotNull(run);
        Assert.Equal(AgentRunStatus.Running, run!.Status);
        Assert.Null(run.FinishedAt);
    }

    // ---- A2.4: chat retention ---------------------------------------------

    [Fact]
    public async Task PHASE145_A3_RetentionDeletesOnlyTurnsPastTheWindow()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        var user = await scenario.CreateUserAsync("Chủ workspace");
        var workspace = await scenario.CreateWorkspaceAsync(user, "Workspace retention");
        var (board, _, _) = await scenario.CreateBoardAsync(workspace);

        var oldId = Guid.NewGuid();
        var freshId = Guid.NewGuid();
        var oldTimestamp = DateTimeOffset.UtcNow.AddDays(-200);

        await scenario.WithDbContextAsync(async db =>
        {
            db.AgentChatMessages.AddRange(
                new AgentChatMessage
                {
                    Id = oldId,
                    BoardId = board.Id,
                    WorkspaceId = workspace.Id,
                    Role = AgentChatRole.User,
                    Content = "Lượt cũ",
                },
                new AgentChatMessage
                {
                    Id = freshId,
                    BoardId = board.Id,
                    WorkspaceId = workspace.Id,
                    Role = AgentChatRole.User,
                    Content = "Lượt mới",
                });

            await db.SaveChangesAsync();

            // `created_at`/`updated_at` are stamped by `SaveChangesAsync` (IAuditableEntity), so the old
            // timestamp must be written afterwards — the same back-dating trick the digest suite uses to
            // make "is this inside the window?" a real question instead of an accident of the clock.
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE agent_chat_messages SET created_at = {0}, updated_at = {0} WHERE id = {1}",
                oldTimestamp,
                oldId);
        });

        var deleted = 0;
        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<AgentChatRetentionRunner>();
            deleted = await runner.RunOnceAsync();
        }

        Assert.Equal(1, deleted);

        // The old row is gone; the fresh one is untouched. This is the whole contract of the sweep.
        Assert.Equal(0, await scenario.CountAsync<AgentChatMessage>(m => m.Id == oldId));
        Assert.Equal(1, await scenario.CountAsync<AgentChatMessage>(m => m.Id == freshId));
    }

    [Fact]
    public async Task PHASE145_A4_RetentionIsBoundedByTheBatchSize()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        var user = await scenario.CreateUserAsync("Chủ workspace");
        var workspace = await scenario.CreateWorkspaceAsync(user, "Workspace retention 2");
        var (board, _, _) = await scenario.CreateBoardAsync(workspace);

        var old = DateTimeOffset.UtcNow.AddDays(-200);

        await scenario.WithDbContextAsync(async db =>
        {
            for (var i = 0; i < 5; i++)
            {
                db.AgentChatMessages.Add(new AgentChatMessage
                {
                    BoardId = board.Id,
                    WorkspaceId = workspace.Id,
                    Role = AgentChatRole.Assistant,
                    Content = $"Lượt {i}",
                });
            }

            await db.SaveChangesAsync();

            // Back-dated after the save: `SaveChangesAsync` stamps `created_at` (see the previous test).
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE agent_chat_messages SET created_at = {0}, updated_at = {0}",
                old);
        });

        var deleted = 0;
        await using (var scope = scenario.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<AgentChatRetentionRunner>();
            deleted = await runner.RunOnceAsync();
        }

        // All five go, but the loop is bounded: the assertion that matters is that the sweep TERMINATES and
        // reports a real count, not that it deletes in one statement.
        Assert.Equal(5, deleted);
        Assert.Equal(0, await scenario.CountAsync<AgentChatMessage>());
    }

    // ---- A1.2: the outbound alert seam ------------------------------------

    [Fact]
    public async Task PHASE145_A5_AMemberAlertReachesTheOutboundSink()
    {
        var sink = new RecordingExternalAlertSink();

        await using var scenario = await _database.CreateScenarioAsync(externalAlerts: sink);

        var owner = await scenario.CreateUserAsync("Chủ workspace");
        var workspace = await scenario.CreateWorkspaceAsync(owner, "Workspace sink");
        var (board, todo, _) = await scenario.CreateBoardAsync(workspace);

        var assignee = await scenario.CreateUserAsync("Người được giao");

        await scenario.WithDbContextAsync(async db =>
        {
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspace.Id,
                UserId = assignee.Id,
                Role = WorkspaceRole.Member,
                MemberType = MemberType.Human,
                JoinedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync();
        });

        using var client = await scenario.AsUserAsync(owner, "Manager");

        // The create path with an assignee is what writes a TaskAssigned notification.
        var response = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/tasks",
            new CreateTaskRequest(
                todo.Id,
                "Thẻ được giao",
                null,
                assignee.Id,
                null,
                null));

        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);

        // The in-app row exists AND the same alert was offered to the transport seam — without the
        // producer knowing a transport exists. That is the whole point of A1.2.
        Assert.Equal(1, await scenario.CountAsync<Notification>(n => n.Type == "TaskAssigned"));
        Assert.True(
            sink.HasType("TaskAssigned"),
            $"The sink recorded: {string.Join(", ", sink.Messages.Select(m => m.Type))}");

        var recorded = sink.Messages.Single(m => m.Type == "TaskAssigned");
        Assert.Equal(workspace.Id, recorded.WorkspaceId);
        Assert.Equal([assignee.Id], recorded.RecipientUserIds);
    }

    [Fact]
    public async Task PHASE145_A6_TheNoOpSinkIsTheProductionDefault()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        // The shared host carries no test double: the production registration is the NoOp sink, which is
        // the intended behaviour with no transport configured — not a degraded mode.
        var sink = scenario.Services.GetRequiredService<IExternalAlertSink>();
        Assert.IsType<NoOpExternalAlertSink>(sink);
    }

    [Fact]
    public async Task PHASE145_A7_TheSeamNeverBreaksTheRequestWhenATransportFails()
    {
        await using var scenario = await _database.CreateScenarioAsync(externalAlerts: new ExplodingAlertSink());

        var owner = await scenario.CreateUserAsync("Chủ workspace");
        var workspace = await scenario.CreateWorkspaceAsync(owner, "Workspace sink lỗi");
        var (board, todo, _) = await scenario.CreateBoardAsync(workspace);
        var assignee = await scenario.CreateUserAsync("Người được giao");

        await scenario.WithDbContextAsync(async db =>
        {
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                WorkspaceId = workspace.Id,
                UserId = assignee.Id,
                Role = WorkspaceRole.Member,
                MemberType = MemberType.Human,
                JoinedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync();
        });

        using var client = await scenario.AsUserAsync(owner, "Manager");

        var response = await client.PostJsonAsync(
            $"/api/boards/{board.Id}/tasks",
            new CreateTaskRequest(todo.Id, "Thẻ vẫn phải tạo được", null, assignee.Id, null, null));

        // 201, not 500: a broken transport must degrade to "in-app only". The task that triggered the alert
        // is already committed, and losing it because Slack was down would be indefensible.
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, await scenario.CountAsync<Notification>(n => n.Type == "TaskAssigned"));
    }

    /// <summary>A transport that always fails — the Phase 15 bad-webhook scenario, used to prove the seam.</summary>
    private sealed class ExplodingAlertSink : IExternalAlertSink
    {
        public Task PublishAsync(ExternalAlertMessage message, CancellationToken ct = default)
            => throw new InvalidOperationException("transport down");
    }
}
