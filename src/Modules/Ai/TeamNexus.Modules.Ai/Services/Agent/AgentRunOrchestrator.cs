using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>
/// The AI Agent Executor (Phase 7 §4.8): the synchronous guard that turns a "Chạy Agent" click into a
/// 202, and the background tool-calling loop that follows it.
/// </summary>
public interface IAgentRunService
{
    /// <summary>
    /// Guards, reserves the task, takes the advisory lock, inserts the <c>Running</c> run row and
    /// schedules the loop in a scope of its own; returns the row for the endpoint's <b>202</b>.
    /// Throws 503/404/403/400/409 per §4.9.
    /// </summary>
    Task<AgentRunResponse> StartAsync(
        Guid taskId, Guid userId, Guid? previousRunId, CancellationToken ct = default);

    /// <summary>Run history of one task, newest first (Member+). <paramref name="take"/> clamps to 1..50.</summary>
    Task<IReadOnlyList<AgentRunResponse>> ListAsync(
        Guid taskId, int take, Guid userId, CancellationToken ct = default);

    /// <summary>One run with its tool trace (Member+).</summary>
    Task<AgentRunDetailResponse> GetAsync(Guid runId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Cancels a <c>Running</c> run: signals the loop and writes the terminal state immediately, so
    /// the response already shows <c>Cancelled</c> (Manager/Admin; 409 when it is not running).
    /// </summary>
    Task<AgentRunDetailResponse> CancelAsync(Guid runId, Guid userId, CancellationToken ct = default);
}

/// <summary>
/// Orchestrator for one agent run. Owns the fixed execution order documented in §4.8 — the loop, the
/// four guardrails, the clarification flow and the single writer of <c>agent_runs</c>.
/// <para>
/// <b>Lock lifetime (deviation from §4.8 step 6, on purpose).</b> The PostgreSQL advisory lock is
/// session-scoped, so it lives on the connection of whichever scope took it. Acquiring it inside the
/// HTTP request would release it the moment that request's scope is disposed — i.e. before the
/// background loop even starts, and in the worst case back into the connection pool with the lock
/// still held. The background scope is therefore created and locked <b>first</b>, and handed to
/// <c>ExecuteAsync</c>, which releases it in <c>finally</c>. The request still returns 409 when the
/// lock cannot be taken.
/// </para>
/// <para>
/// <b>Append-only (D14).</b> "Chạy lại" inserts a NEW row; the re-run records which human answer it
/// consumed (<c>resolution_comment_id</c>) and the old row is never touched — that is what keeps the
/// previous <c>QuestionAsked</c> auditable after the next attempt succeeds.
/// </para>
/// </summary>
public sealed class AgentRunOrchestrator : IAgentRunService
{
    public const int DefaultRunsTake = 20;

    public const int MaxRunsTake = 50;

    /// <summary>Replacement text for tool results dropped by history compaction (must keep the message).</summary>
    public const string CompactionPlaceholder = "[nội dung cũ đã lược bỏ để tiết kiệm token]";

    private const string RunBusyMessage = "An agent run is already in progress for this task.";

    /// <summary>Matches the <c>agent_runs.error</c> / <c>clarification_question</c> column limits.</summary>
    private const int MaxRunErrorLength = 2000;

    private const double ChatTemperature = 0.2;

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IAiAgentResolver _agents;
    private readonly IAiToolCallingProvider _provider;
    private readonly IAgentToolRegistry _tools;
    private readonly IAgentOutputService _outputs;
    private readonly ICommentService _comments;
    private readonly ITaskService _tasks;
    private readonly IBoardEventPublisher _events;
    private readonly INotificationService _notifications;
    private readonly AgentRunCancellationRegistry _cancellations;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentOptions _options;
    private readonly DeepSeekOptions _aiOptions;
    private readonly ILogger<AgentRunOrchestrator> _logger;

    public AgentRunOrchestrator(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IAiAgentResolver agents,
        IAiToolCallingProvider provider,
        IAgentToolRegistry tools,
        IAgentOutputService outputs,
        ICommentService comments,
        ITaskService tasks,
        IBoardEventPublisher events,
        INotificationService notifications,
        AgentRunCancellationRegistry cancellations,
        IServiceScopeFactory scopeFactory,
        IOptions<AgentOptions> options,
        IOptions<DeepSeekOptions> aiOptions,
        ILogger<AgentRunOrchestrator> logger)
    {
        _db = db;
        _access = access;
        _agents = agents;
        _provider = provider;
        _tools = tools;
        _outputs = outputs;
        _comments = comments;
        _tasks = tasks;
        _events = events;
        _notifications = notifications;
        _cancellations = cancellations;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _aiOptions = aiOptions.Value;
        _logger = logger;
    }

    private AgentEffectiveOptions Effective => _options.Effective;

    // =====================================================================
    // StartAsync — the synchronous guard (runs inside the HTTP request)
    // =====================================================================

    public async Task<AgentRunResponse> StartAsync(
        Guid taskId, Guid userId, Guid? previousRunId, CancellationToken ct = default)
    {
        // 1) Disabled ⇒ 503 BEFORE touching the database (precedent: ReportingDisabledException).
        if (!_options.Enabled)
        {
            throw new AgentDisabledException();
        }

        // 2) Task + board.
        var task = await _db.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == task.BoardId, ct)
            ?? throw new NotFoundException("Board not found.");

        // 3) Manager/Admin only (404 for an invisible workspace, 403 for a plain Member).
        await _access.RequireManagerAsync(board.WorkspaceId, userId, ct);

        // 4) The task must already be assigned to this workspace's agent. The endpoint never assigns
        //    it on the user's behalf: one click = one action.
        var agentUserId = await ResolveAgentAssigneeAsync(board.WorkspaceId, task.AssigneeId, ct);

        // 5) Rerun guard: same task, stopped on a clarification question, with an answer to consume.
        AgentRun? previousRun = null;
        Guid? resolutionCommentId = null;

        if (previousRunId is { } previousId)
        {
            previousRun = await _db.AgentRuns
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == previousId && r.TaskId == taskId, ct)
                ?? throw new BadRequestException("The previous run does not belong to this task.");

            if (previousRun.Status != AgentRunStatus.AwaitingClarification
                || previousRun.ClarificationCommentId is null)
            {
                throw new BadRequestException("Only a run waiting for clarification can be re-run.");
            }

            resolutionCommentId = await FindResolutionCommentAsync(previousRun, agentUserId, ct)
                ?? throw new BadRequestException("No answer found for the clarification request.");
        }

        // 6) Cross-instance view: a Running row for this task makes the click a conflict.
        if (!_cancellations.TryReserveTask(taskId))
        {
            throw new ConflictException(RunBusyMessage);
        }

        AsyncServiceScope? scope = null;

        try
        {
            var alreadyRunning = await _db.AgentRuns
                .AsNoTracking()
                .AnyAsync(r => r.TaskId == taskId && r.Status == AgentRunStatus.Running, ct);

            if (alreadyRunning)
            {
                throw new ConflictException(RunBusyMessage);
            }

            // 7) Background scope + advisory lock (see the class remarks for why the lock lives here).
            scope = _scopeFactory.CreateAsyncScope();
            var background = scope.Value.ServiceProvider.GetRequiredService<AgentRunOrchestrator>();

            if (!await background.TryAcquireTaskLockAsync(taskId, ct))
            {
                await scope.Value.DisposeAsync();
                scope = null;
                throw new ConflictException(RunBusyMessage);
            }

            // 8) Insert the run row.
            var now = DateTimeOffset.UtcNow;
            var run = new AgentRun
            {
                Id = Guid.NewGuid(),
                WorkspaceId = board.WorkspaceId,
                BoardId = board.Id,
                TaskId = taskId,
                AgentUserId = agentUserId,
                TriggeredByUserId = userId,
                Status = AgentRunStatus.Running,
                StartedAt = now,
                PreviousRunId = previousRun?.Id,
                // Consumed by THIS run — the previous row stays byte-identical (D14, verify group E).
                ResolutionCommentId = resolutionCommentId,
                ToolCallTrace = "[]",
            };

            _db.AgentRuns.Add(run);
            await _db.SaveChangesAsync(ct);

            // 9) Cancellation handle + hand-off to a scope that is NOT the request's.
            var runCts = new CancellationTokenSource();
            _cancellations.Register(run.Id, runCts);

            var ownsScope = scope.Value;
            scope = null;

            _ = Task.Run(
                () => background.ExecuteAsync(run.Id, taskId, ownsScope, runCts.Token),
                CancellationToken.None);

            // 10) Broadcast + 202 body.
            await BroadcastAsync(
                run, AgentRunStatus.Running, run.StopReason, toolCalls: 0, totalTokens: 0, clarification: null, ct);

            var names = await LoadUserNamesAsync([agentUserId, userId], ct);

            _logger.LogInformation(
                "Agent run {RunId} started for task {TaskId} by user {UserId} (agent {AgentUserId}).",
                run.Id, taskId, userId, agentUserId);

            return AgentRunResponse.From(run, names.GetValueOrDefault(agentUserId), names.GetValueOrDefault(userId));
        }
        catch
        {
            // Never leak the reservation, and never leave a locked scope behind.
            if (scope is { } loose)
            {
                var instance = loose.ServiceProvider.GetService<AgentRunOrchestrator>();
                if (instance is not null)
                {
                    await instance.ReleaseTaskLockAsync();
                }

                await loose.DisposeAsync();
            }

            _cancellations.ReleaseTask(taskId);
            throw;
        }
    }

    // =====================================================================
    // ExecuteAsync — the background loop
    // =====================================================================

    /// <summary>
    /// Runs one agent loop to its terminal state. Public so the background scope can resolve the
    /// concrete type; never called from an HTTP handler. Never throws: nothing awaits this task.
    /// </summary>
    public async Task ExecuteAsync(
        Guid runId, Guid taskId, AsyncServiceScope scope, CancellationToken ct)
    {
        try
        {
            await RunLoopAsync(runId, taskId, ct);
        }
        catch (OperationCanceledException)
        {
            // The loop writes its own terminal state for cancellations; this is the SETUP-phase safety
            // net (e.g. a query cancelled before the loop started), so a run can never stay Running
            // forever waiting for the reaper. It is a no-op when the row is already settled.
            await TryMarkFailedAsync(
                runId,
                ct.IsCancellationRequested ? AgentStopReason.Cancelled : AgentStopReason.TimeLimit,
                "Lượt chạy bị dừng trong lúc chuẩn bị (chưa vào vòng lặp).",
                notify: !ct.IsCancellationRequested);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent run {RunId} failed with an unhandled exception.", runId);
            await TryMarkFailedAsync(runId, AgentStopReason.InternalError, ex.Message, notify: true);
        }
        finally
        {
            _cancellations.ReleaseTask(taskId);

            if (_cancellations.Take(runId) is { } cts)
            {
                cts.Dispose();
            }

            await ReleaseTaskLockAsync();
            await scope.DisposeAsync();
        }
    }

    private async Task RunLoopAsync(Guid runId, Guid taskId, CancellationToken ct)
    {
        // A run cancelled before the loop started (or closed by the reaper) must not run at all.
        var run = await _db.AgentRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId, ct);

        if (run is null || run.Status != AgentRunStatus.Running)
        {
            _logger.LogInformation(
                "Agent run {RunId} is no longer Running before execution started; skipping.", runId);
            return;
        }

        var effective = Effective;

        var task = await _db.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);

        if (task is null)
        {
            await TryMarkFailedAsync(runId, AgentStopReason.TaskChanged, "Task not found.", notify: true);
            return;
        }

        var completion = await RunToolLoopAsync(run, task, effective, ct);
        await FinalizeAsync(run, task, completion, ct);
    }

    /// <summary>Result of the loop: why it stopped, what it produced and the counters to persist.</summary>
    private sealed record LoopCompletion(
        AgentStopReason Reason,
        string? Error,
        AgentDraft? Draft,
        string? Question,
        int ToolCalls,
        int LlmCalls,
        int PromptTokens,
        int CompletionTokens,
        string ToolTrace,
        bool TraceTruncated);

    private async Task<LoopCompletion> RunToolLoopAsync(
        AgentRun run, BoardTask task, AgentEffectiveOptions effective, CancellationToken external)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(external);
        timeoutCts.CancelAfter(_options.RunTimeout);
        var token = timeoutCts.Token;

        var systemPrompt = AgentPrompts.BuildSystemPrompt();
        var comments = await LoadPromptCommentsAsync(run.TaskId, run.ResolutionCommentId, effective, token);
        var previousQuestion = run.PreviousRunId is null
            ? null
            : await _db.AgentRuns
                .AsNoTracking()
                .Where(r => r.Id == run.PreviousRunId)
                .Select(r => r.ClarificationQuestion)
                .FirstOrDefaultAsync(token);

        var resolutionComment = run.ResolutionCommentId is { } resolutionId
            ? await _db.TaskComments
                .AsNoTracking()
                .Where(c => c.Id == resolutionId)
                .Select(c => c.Content)
                .FirstOrDefaultAsync(token)
            : null;

        var columnName = await _db.BoardColumns
            .AsNoTracking()
            .Where(c => c.Id == task.ColumnId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(token) ?? string.Empty;

        var promptContext = new AgentPromptContext(
            task.Id,
            task.Title,
            task.Description,
            columnName,
            null,
            task.Priority?.ToString(),
            task.DueDate,
            task.CreatedAt,
            task.UpdatedAt,
            comments,
            previousQuestion,
            resolutionComment);

        var messages = new List<AiChatMessage>
        {
            new("user", AgentPrompts.BuildUserPrompt(promptContext, effective)),
        };

        var toolContext = new AgentToolContext(
            run.WorkspaceId, run.BoardId, run.TaskId, run.AgentUserId, run.Id);

        var stopwatch = Stopwatch.StartNew();
        var trace = run.ToolCallTrace;
        var traceTruncated = run.TraceTruncated;
        var toolCalls = 0;
        var llmCalls = 0;
        var promptTokens = 0;
        var completionTokens = 0;

        var reason = AgentStopReason.InternalError;
        string? error = null;

        while (true)
        {
            var state = new AgentGuardrailState(toolCalls, llmCalls, promptTokens, completionTokens, stopwatch.Elapsed);

            // 1) Anti-infinite-loop guard, checked BEFORE spending another call.
            if (!AgentGuardrails.CanCallProvider(state, effective))
            {
                reason = AgentStopReason.InternalError;
                error = $"Vượt Agent:MaxRunLlmCalls={effective.MaxRunLlmCalls}: model không kết thúc sau {llmCalls} lượt.";
                break;
            }

            // 2) One provider round.
            AiChatResult result;
            try
            {
                result = await _provider.ChatAsync(
                    new AiChatRequest(
                        systemPrompt,
                        messages,
                        _tools.Definitions,
                        ChatTemperature,
                        _aiOptions.MaxTokens),
                    token);
            }
            catch (AiProviderException ex)
            {
                reason = AgentStopReason.ProviderError;
                error = ex.Message;
                break;
            }
            catch (OperationCanceledException)
            {
                (reason, error, _, _) = ClassifyCancellation(external, timeoutCts, state, effective);
                break;
            }

            llmCalls++;
            promptTokens += result.PromptTokens ?? 0;
            completionTokens += result.CompletionTokens ?? 0;

            state = new AgentGuardrailState(toolCalls, llmCalls, promptTokens, completionTokens, stopwatch.Elapsed);

            // 4) Guardrails after the call (fixed precedence, D12).
            if (AgentGuardrails.Evaluate(state, effective) is { } exceeded)
            {
                reason = exceeded;
                error = AgentGuardrails.Describe(exceeded, state, effective);
                break;
            }

            // 5) A turn without tool calls: the agent is expected to finish through a tool.
            if (result.ToolCalls.Count == 0)
            {
                if (string.Equals(result.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
                {
                    reason = AgentStopReason.TokenBudget;
                    error = AgentGuardrails.Describe(AgentStopReason.TokenBudget, state, effective);
                }
                else if (string.IsNullOrWhiteSpace(result.Content))
                {
                    reason = AgentStopReason.InternalError;
                    error = "Model trả về lượt trống (không content, không tool_calls).";
                }
                else
                {
                    reason = AgentStopReason.InternalError;
                    error = "Model tự kết thúc mà không gọi DraftOutput/RequestClarification.";
                }

                break;
            }

            // 6) Echo the assistant turn (with its tool calls) so the history stays valid.
            messages.Add(new AiChatMessage("assistant", result.Content, ToolCalls: result.ToolCalls));

            var stopAfterBatch = false;

            foreach (var call in result.ToolCalls)
            {
                var callStopwatch = Stopwatch.StartNew();
                AgentToolOutcome outcome;
                try
                {
                    outcome = await _tools.DispatchAsync(call.Name, call.ArgumentsJson, toolContext, token);
                }
                catch (OperationCanceledException)
                {
                    (reason, error, _, _) = ClassifyCancellation(external, timeoutCts, state, effective);
                    stopAfterBatch = true;
                    break;
                }

                callStopwatch.Stop();
                toolCalls++;

                var entry = AgentAttachmentFactory.BuildToolTraceEntry(
                    call.Name, call.ArgumentsJson, outcome.ResultJson, outcome.IsError,
                    DateTimeOffset.UtcNow, callStopwatch.ElapsedMilliseconds, effective.ToolTraceResultChars);

                trace = AgentAttachmentFactory.AppendToolTrace(
                    trace, entry, effective.ToolTraceMaxEntries, out var truncated);
                traceTruncated |= truncated;

                messages.Add(new AiChatMessage("tool", outcome.ResultJson, ToolCallId: call.Id, Name: call.Name));

                // 7) Guardrail immediately after EACH tool, not at the end of the batch. It is checked
                //    before the stop signal so a tool call that pushed the run over budget really is
                //    reported as such instead of silently succeeding.
                state = new AgentGuardrailState(toolCalls, llmCalls, promptTokens, completionTokens, stopwatch.Elapsed);

                if (AgentGuardrails.Evaluate(state, effective) is { } afterTool)
                {
                    reason = afterTool;
                    error = AgentGuardrails.Describe(afterTool, state, effective);
                    stopAfterBatch = true;
                    break;
                }

                if (outcome.StopLoop)
                {
                    reason = outcome.StopReason ?? AgentStopReason.InternalError;
                    stopAfterBatch = true;
                    break;
                }
            }

            if (stopAfterBatch)
            {
                break;
            }

            // 8) History compaction: original messages stay (the assistant/tool_call_id pairing must
            //    remain valid), only the oldest tool BODY is replaced.
            CompactToolResults(messages, effective);

            // Live progress: persisted every turn so a cancel, a crash or a Kanban refresh mid-run
            // still shows real counters instead of zeros.
            await PersistProgressAsync(run.Id, toolCalls, llmCalls, promptTokens, completionTokens, trace, traceTruncated);
        }

        stopwatch.Stop();

        return new LoopCompletion(
            reason, Cap(error, MaxRunErrorLength), toolContext.Draft, toolContext.ClarificationQuestion,
            toolCalls, llmCalls, promptTokens, completionTokens, trace, traceTruncated);
    }

    /// <summary>
    /// Timeout vs human cancel. The linked CTS is cancelled by EITHER source, so the caller's own token
    /// has to be inspected first — otherwise a user cancel would be reported as a wall-clock overrun.
    /// </summary>
    private static (AgentStopReason Reason, string? Error, bool Cancelled, bool TimedOut) ClassifyCancellation(
        CancellationToken external,
        CancellationTokenSource timeoutCts,
        AgentGuardrailState state,
        AgentEffectiveOptions effective)
    {
        if (external.IsCancellationRequested)
        {
            return (AgentStopReason.Cancelled, "Lượt chạy đã bị con người huỷ.", true, false);
        }

        if (timeoutCts.IsCancellationRequested)
        {
            return (
                AgentStopReason.TimeLimit,
                AgentGuardrails.Describe(AgentStopReason.TimeLimit, state, effective),
                false,
                true);
        }

        return (AgentStopReason.Cancelled, "Lượt chạy đã dừng.", true, false);
    }

    // =====================================================================
    // Terminal state
    // =====================================================================

    private async Task FinalizeAsync(
        AgentRun run, BoardTask task, LoopCompletion completion, CancellationToken ct)
    {
        // e) Re-check the task before writing anything (TaskChanged).
        if (completion.Reason is AgentStopReason.DraftProduced or AgentStopReason.QuestionAsked
            && !await IsTaskStillEligibleAsync(run, ct))
        {
            completion = completion with
            {
                Reason = AgentStopReason.TaskChanged,
                Error = "Task đã thay đổi trong lúc agent chạy (bị xoá, đổi người thực hiện hoặc rời cột 'Chờ làm rõ').",
                Draft = null,
                Question = null,
            };
        }

        switch (completion.Reason)
        {
            case AgentStopReason.DraftProduced when completion.Draft is not null:
                await FinalizeDraftAsync(run, completion, ct);
                break;

            case AgentStopReason.QuestionAsked when completion.Question is not null:
                await FinalizeClarificationAsync(run, completion, ct);
                break;

            default:
                await FinalizeFailureAsync(run, completion, ct);
                break;
        }

        await BroadcastTerminalAsync(run.Id, ct);
    }

    /// <summary>
    /// Draft path (D9): the Pending action and the run's <c>AwaitingApproval</c> transition share ONE
    /// transaction, so a cancel that wins the CAS race cannot leave an orphan Pending action behind.
    /// </summary>
    private async Task FinalizeDraftAsync(AgentRun run, LoopCompletion completion, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var output = await _outputs.CreatePendingAsync(run, completion.Draft!, ct);

        if (output.LogId is null)
        {
            await transaction.RollbackAsync(ct);

            await FinalizeFailureAsync(
                run,
                completion with { Reason = AgentStopReason.InternalError, Error = output.Error },
                ct);

            return;
        }

        var affected = await _db.AgentRuns
            .Where(r => r.Id == run.Id && r.Status == AgentRunStatus.Running)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, AgentRunStatus.AwaitingApproval)
                .SetProperty(r => r.StopReason, AgentStopReason.DraftProduced)
                .SetProperty(r => r.OutputKind, output.Kind)
                .SetProperty(r => r.AiActionLogId, output.LogId)
                .SetProperty(r => r.ToolCallCount, completion.ToolCalls)
                .SetProperty(r => r.LlmCallCount, completion.LlmCalls)
                .SetProperty(r => r.PromptTokens, completion.PromptTokens)
                .SetProperty(r => r.CompletionTokens, completion.CompletionTokens)
                .SetProperty(r => r.ToolCallTrace, completion.ToolTrace)
                .SetProperty(r => r.TraceTruncated, completion.TraceTruncated)
                .SetProperty(r => r.UpdatedAt, now), ct);

        if (affected == 0)
        {
            // The human cancelled (or the reaper closed it) between the output and this update: roll the
            // Pending action back instead of leaving a decision nobody can trace to a live run.
            await transaction.RollbackAsync(ct);

            _logger.LogInformation(
                "Agent run {RunId}: cancelled while producing its output; the Pending action was rolled back.",
                run.Id);

            return;
        }

        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "Agent run {RunId} awaiting approval (kind {Kind}, action {LogId}).",
            run.Id, output.Kind, output.LogId);

        await NotifyAsync(run.Id, AgentNotificationTypes.OutputPending, ct);
    }

    /// <summary>
    /// Clarification path (D10): posts the question as an agent comment, moves the task into the
    /// lazily created "Chờ làm rõ" column and parks the run. Accepted narrow race: if a cancel lands
    /// between the comment and the CAS, the question stays visible while the run reads
    /// <c>Cancelled</c> — recoverable by answering and pressing "Chạy lại".
    /// </summary>
    private async Task FinalizeClarificationAsync(
        AgentRun run, LoopCompletion completion, CancellationToken ct)
    {
        var question = completion.Question!;
        Guid? commentId = null;

        try
        {
            var comment = await _comments.CreateCommentAsync(
                run.TaskId, new CreateCommentRequest(question), run.AgentUserId, ct);
            commentId = comment.Id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Agent run {RunId}: could not post the clarification comment.", run.Id);
        }

        try
        {
            var columnId = await _agents.EnsureClarificationColumnAsync(run.BoardId, ct);

            // int.MaxValue: TaskService clamps to the end of the column, which is where a new question
            // belongs. Acting user is the manager so the move is audited against a real person.
            await _tasks.MoveTaskAsync(
                run.TaskId,
                new MoveTaskRequest(columnId, int.MaxValue),
                run.TriggeredByUserId,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Agent run {RunId}: could not move the task into the clarification column.", run.Id);
        }

        var now = DateTimeOffset.UtcNow;
        var affected = await _db.AgentRuns
            .Where(r => r.Id == run.Id && r.Status == AgentRunStatus.Running)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, AgentRunStatus.AwaitingClarification)
                .SetProperty(r => r.StopReason, AgentStopReason.QuestionAsked)
                .SetProperty(r => r.ClarificationQuestion, question)
                .SetProperty(r => r.ClarificationCommentId, commentId)
                .SetProperty(r => r.ToolCallCount, completion.ToolCalls)
                .SetProperty(r => r.LlmCallCount, completion.LlmCalls)
                .SetProperty(r => r.PromptTokens, completion.PromptTokens)
                .SetProperty(r => r.CompletionTokens, completion.CompletionTokens)
                .SetProperty(r => r.ToolCallTrace, completion.ToolTrace)
                .SetProperty(r => r.TraceTruncated, completion.TraceTruncated)
                .SetProperty(r => r.UpdatedAt, now), ct);

        if (affected == 0)
        {
            _logger.LogInformation(
                "Agent run {RunId}: reached a terminal state elsewhere while asking its question.", run.Id);
            return;
        }

        _logger.LogInformation("Agent run {RunId} awaiting clarification (comment {CommentId}).", run.Id, commentId);

        await NotifyAsync(run.Id, AgentNotificationTypes.AwaitingClarification, ct);
    }

    /// <summary>
    /// Failure path: one CAS, then exactly ONE notification when the CAS actually won (the
    /// <c>notification_sent</c> flag is what keeps a later GET from warning again — D12, verify group F).
    /// </summary>
    private async Task FinalizeFailureAsync(AgentRun run, LoopCompletion completion, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var reason = completion.Reason;

        var affected = await _db.AgentRuns
            .Where(r => r.Id == run.Id && r.Status == AgentRunStatus.Running)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, AgentRunStatus.Failed)
                .SetProperty(r => r.StopReason, reason)
                .SetProperty(r => r.Error, completion.Error)
                .SetProperty(r => r.NotificationSent, reason != AgentStopReason.Cancelled)
                .SetProperty(r => r.ToolCallCount, completion.ToolCalls)
                .SetProperty(r => r.LlmCallCount, completion.LlmCalls)
                .SetProperty(r => r.PromptTokens, completion.PromptTokens)
                .SetProperty(r => r.CompletionTokens, completion.CompletionTokens)
                .SetProperty(r => r.ToolCallTrace, completion.ToolTrace)
                .SetProperty(r => r.TraceTruncated, completion.TraceTruncated)
                .SetProperty(r => r.FinishedAt, now)
                .SetProperty(r => r.UpdatedAt, now), ct);

        if (affected == 0)
        {
            _logger.LogInformation(
                "Agent run {RunId} was already terminal (probably cancelled); the failure state was not applied.",
                run.Id);
            return;
        }

        _logger.LogWarning(
            "Agent run {RunId} failed: {Reason} ({Error}) after {ToolCalls} tool call(s), {Tokens} token(s).",
            run.Id, reason, completion.Error, completion.ToolCalls, completion.PromptTokens + completion.CompletionTokens);

        // Cancelled must NOT notify (D12): the person who pressed cancel knows.
        if (reason != AgentStopReason.Cancelled)
        {
            await NotifyAsync(run.Id, AgentNotificationTypes.RunFailed, ct);
        }
    }

    /// <summary>Best-effort terminal state for an unexpected exception (nothing awaits the loop).</summary>
    private async Task TryMarkFailedAsync(Guid runId, AgentStopReason reason, string? error, bool notify)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var affected = await _db.AgentRuns
                .Where(r => r.Id == runId && r.Status == AgentRunStatus.Running)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.Status, AgentRunStatus.Failed)
                    .SetProperty(r => r.StopReason, reason)
                    .SetProperty(r => r.Error, Cap(error, MaxRunErrorLength))
                    .SetProperty(r => r.NotificationSent, notify)
                    .SetProperty(r => r.FinishedAt, now)
                    .SetProperty(r => r.UpdatedAt, now), CancellationToken.None);

            if (affected > 0 && notify)
            {
                await NotifyAsync(runId, AgentNotificationTypes.RunFailed, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not mark agent run {RunId} as failed.", runId);
        }
    }

    // =====================================================================
    // Read / cancel (routes 3, 4, 5)
    // =====================================================================

    public async Task<IReadOnlyList<AgentRunResponse>> ListAsync(
        Guid taskId, int take, Guid userId, CancellationToken ct = default)
    {
        var task = await _db.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == task.BoardId, ct)
            ?? throw new NotFoundException("Board not found.");

        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);

        var limit = take <= 0 ? DefaultRunsTake : Math.Clamp(take, 1, MaxRunsTake);

        var runs = await _db.AgentRuns
            .AsNoTracking()
            .Where(r => r.TaskId == taskId)
            .OrderByDescending(r => r.StartedAt)
            .Take(limit)
            .ToListAsync(ct);

        var names = await LoadUserNamesAsync(
            runs.SelectMany(r => new[] { r.AgentUserId, r.TriggeredByUserId }), ct);

        return runs
            .Select(r => AgentRunResponse.From(
                r,
                names.GetValueOrDefault(r.AgentUserId),
                names.GetValueOrDefault(r.TriggeredByUserId)))
            .ToList();
    }

    public async Task<AgentRunDetailResponse> GetAsync(
        Guid runId, Guid userId, CancellationToken ct = default)
    {
        var run = await RequireVisibleRunAsync(runId, userId, ct);
        return await BuildDetailAsync(run, ct);
    }

    public async Task<AgentRunDetailResponse> CancelAsync(
        Guid runId, Guid userId, CancellationToken ct = default)
    {
        // 503 before anything else, for all three WRITE routes (verify group H). A disabled feature
        // must not reveal whether a run exists, and "cancelled" would be a lie if nothing runs.
        if (!_options.Enabled)
        {
            throw new AgentDisabledException();
        }

        var run = await LoadRunAsync(runId, ct);

        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == run.BoardId, ct)
            ?? throw new NotFoundException("Board not found.");

        await _access.RequireManagerAsync(board.WorkspaceId, userId, ct);

        if (run.Status != AgentRunStatus.Running)
        {
            throw new ConflictException("Only a Running agent run can be cancelled.");
        }

        // Signals the loop (stops the in-flight HTTP call), then writes the terminal state here so the
        // response is immediately truthful instead of polling for the loop to notice.
        var signalled = _cancellations.TryCancel(runId);

        var now = DateTimeOffset.UtcNow;
        var affected = await _db.AgentRuns
            .Where(r => r.Id == runId && r.Status == AgentRunStatus.Running)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, AgentRunStatus.Failed)
                .SetProperty(r => r.StopReason, AgentStopReason.Cancelled)
                .SetProperty(r => r.Error, (string?)"Lượt chạy đã bị con người huỷ.")
                .SetProperty(r => r.NotificationSent, false)
                .SetProperty(r => r.FinishedAt, now)
                .SetProperty(r => r.UpdatedAt, now), ct);

        if (affected == 0)
        {
            throw new ConflictException("Only a Running agent run can be cancelled.");
        }

        if (!signalled)
        {
            // Known limitation (D15, no distributed queue): another instance owns the loop, so the row
            // is settled but its HTTP call keeps running there until it finishes on its own.
            _logger.LogWarning(
                "Agent run {RunId} was cancelled in the database but this instance holds no loop for it.",
                runId);
        }

        _logger.LogInformation("Agent run {RunId} cancelled by user {UserId}.", runId, userId);

        var settled = await LoadRunAsync(runId, ct);
        return await BuildDetailAsync(settled, ct);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private async Task<AgentRun> LoadRunAsync(Guid runId, CancellationToken ct)
        => await _db.AgentRuns
               .AsNoTracking()
               .FirstOrDefaultAsync(r => r.Id == runId, ct)
           ?? throw new NotFoundException("Agent run not found.");

    private async Task<AgentRun> RequireVisibleRunAsync(Guid runId, Guid userId, CancellationToken ct)
    {
        var run = await LoadRunAsync(runId, ct);

        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == run.BoardId, ct)
            ?? throw new NotFoundException("Board not found.");

        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);
        return run;
    }

    private async Task<AgentRunDetailResponse> BuildDetailAsync(AgentRun run, CancellationToken ct)
    {
        var names = await LoadUserNamesAsync([run.AgentUserId, run.TriggeredByUserId], ct);

        string? previousQuestion = null;
        if (run.PreviousRunId is { } previousId)
        {
            previousQuestion = await _db.AgentRuns
                .AsNoTracking()
                .Where(r => r.Id == previousId)
                .Select(r => r.ClarificationQuestion)
                .FirstOrDefaultAsync(ct);
        }

        string? resolutionContent = null;
        if (run.ResolutionCommentId is { } resolutionId)
        {
            // IgnoreQueryFilters: an answer that was soft-deleted afterwards is still part of the trail.
            resolutionContent = await _db.TaskComments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.Id == resolutionId)
                .Select(c => c.Content)
                .FirstOrDefaultAsync(ct);
        }

        return AgentRunDetailResponse.From(
            run,
            names.GetValueOrDefault(run.AgentUserId),
            names.GetValueOrDefault(run.TriggeredByUserId),
            AgentToolTraceParser.Parse(run.ToolCallTrace),
            previousQuestion,
            resolutionContent);
    }

    /// <summary>The agent of this workspace, asserted to be the task's assignee (400 otherwise).</summary>
    private async Task<Guid> ResolveAgentAssigneeAsync(
        Guid workspaceId, Guid? assigneeId, CancellationToken ct)
    {
        if (assigneeId is null || !await _agents.IsAiAgentAsync(workspaceId, assigneeId.Value, ct))
        {
            throw new BadRequestException("Task is not assigned to the AI Agent.");
        }

        return assigneeId.Value;
    }

    /// <summary>Newest human comment after the previous run stopped — the answer a re-run consumes.</summary>
    private async Task<Guid?> FindResolutionCommentAsync(
        AgentRun previousRun, Guid agentUserId, CancellationToken ct)
    {
        var since = previousRun.FinishedAt ?? previousRun.StartedAt;

        return await _db.TaskComments
            .AsNoTracking()
            .Where(c => c.TaskId == previousRun.TaskId
                        && c.AuthorId != agentUserId
                        && c.CreatedAt > since)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// A run may only deliver its result while the task is still the same work: still assigned to the
    /// agent, and — for a re-run — still sitting in the clarification column.
    /// </summary>
    private async Task<bool> IsTaskStillEligibleAsync(AgentRun run, CancellationToken ct)
    {
        var task = await _db.Tasks
            .AsNoTracking()
            .Where(t => t.Id == run.TaskId)
            .Select(t => new { t.AssigneeId, t.ColumnId })
            .FirstOrDefaultAsync(ct);

        if (task is null || task.AssigneeId != run.AgentUserId)
        {
            return false;
        }

        if (run.PreviousRunId is null)
        {
            return true;
        }

        return await _db.BoardColumns
            .AsNoTracking()
            .AnyAsync(c => c.Id == task.ColumnId && c.IsClarification, ct);
    }

    private async Task<List<AgentContextComment>> LoadPromptCommentsAsync(
        Guid taskId, Guid? excludedCommentId, AgentEffectiveOptions effective, CancellationToken ct)
    {
        var take = Math.Max(1, effective.MaxCommentsInContext);

        var rows = await _db.TaskComments
            .AsNoTracking()
            .Where(c => c.TaskId == taskId && (excludedCommentId == null || c.Id != excludedCommentId))
            .OrderByDescending(c => c.CreatedAt)
            .Take(take)
            .Select(c => new AgentContextComment(
                c.Author != null ? c.Author.DisplayName : string.Empty,
                c.CreatedAt,
                c.Content))
            .ToListAsync(ct);

        // Newest-first from the database, oldest-first for the prompt.
        rows.Reverse();
        return rows;
    }

    /// <summary>
    /// Replaces the oldest tool RESULT bodies with a placeholder once the history exceeds
    /// <c>Agent:MaxTotalToolResultChars</c>. Messages are never removed: doing so would break the
    /// <c>assistant.tool_calls</c> ↔ <c>tool.tool_call_id</c> pairing and the provider would reject
    /// the next request.
    /// </summary>
    public static void CompactToolResults(List<AiChatMessage> messages, AgentEffectiveOptions options)
    {
        for (var index = 0; index < messages.Count; index++)
        {
            if (TotalToolChars(messages) <= options.MaxTotalToolResultChars)
            {
                return;
            }

            var message = messages[index];
            if (!string.Equals(message.Role, "tool", StringComparison.Ordinal)
                || message.Content == CompactionPlaceholder)
            {
                continue;
            }

            messages[index] = message with { Content = CompactionPlaceholder };
        }
    }

    private static int TotalToolChars(List<AiChatMessage> messages)
        => messages
            .Where(m => string.Equals(m.Role, "tool", StringComparison.Ordinal))
            .Sum(m => m.Content?.Length ?? 0);

    private async Task PersistProgressAsync(
        Guid runId, int toolCalls, int llmCalls, int promptTokens, int completionTokens,
        string trace, bool traceTruncated)
    {
        try
        {
            await _db.AgentRuns
                .Where(r => r.Id == runId && r.Status == AgentRunStatus.Running)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.ToolCallCount, toolCalls)
                    .SetProperty(r => r.LlmCallCount, llmCalls)
                    .SetProperty(r => r.PromptTokens, promptTokens)
                    .SetProperty(r => r.CompletionTokens, completionTokens)
                    .SetProperty(r => r.ToolCallTrace, trace)
                    .SetProperty(r => r.TraceTruncated, traceTruncated)
                    .SetProperty(r => r.UpdatedAt, DateTimeOffset.UtcNow), CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Progress is best-effort: the terminal write at the end is authoritative.
            _logger.LogWarning(ex, "Agent run {RunId}: could not persist live progress.", runId);
        }
    }

    private async Task BroadcastAsync(
        AgentRun run, AgentRunStatus status, AgentStopReason? stopReason,
        int toolCalls, int totalTokens, string? clarification, CancellationToken ct)
    {
        try
        {
            await _events.AgentRunProgress(
                run.BoardId,
                new AgentRunProgressEventPayload(
                    run.Id, run.TaskId, run.BoardId, status.ToString(), stopReason?.ToString(),
                    toolCalls, totalTokens, clarification),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not broadcast AgentRunProgress for run {RunId}.", run.Id);
        }
    }

    /// <summary>
    /// Terminal broadcast. The payload is re-read from the database so it can never disagree with the
    /// row (e.g. when a cancel won the CAS race and the loop's intended state was rejected).
    /// </summary>
    private async Task BroadcastTerminalAsync(Guid runId, CancellationToken ct)
    {
        try
        {
            var settled = await _db.AgentRuns
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == runId, CancellationToken.None);

            if (settled is null)
            {
                return;
            }

            await BroadcastAsync(
                settled, settled.Status, settled.StopReason, settled.ToolCallCount,
                settled.PromptTokens + settled.CompletionTokens, settled.ClarificationQuestion, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not broadcast the terminal AgentRunProgress for run {RunId}.", runId);
        }
    }

    private async Task NotifyAsync(Guid runId, string type, CancellationToken ct)
    {
        try
        {
            var run = await _db.AgentRuns
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == runId, ct);

            if (run is null)
            {
                return;
            }

            var notification = new AgentNotification(
                type,
                AgentNotificationFactory.BuildTitle(type),
                AgentNotificationFactory.BuildMessage(type, run, Effective),
                AgentNotificationFactory.BuildPayload(run, Effective));

            await _notifications.NotifyManagersAsync(run.WorkspaceId, notification, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed alert must never fail the run that produced it.
            _logger.LogWarning(ex, "Could not write the {Type} notification for agent run {RunId}.", type, runId);
        }
    }

    private async Task<Dictionary<Guid, string>> LoadUserNamesAsync(
        IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await _db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
    }

    private static string? Cap(string? value, int limit)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= limit ? value : value[..limit];
    }

    // ---- PostgreSQL advisory lock (per task) --------------------------------

    /// <summary>
    /// Session-level lock keyed by the task: must be acquired on the connection of the scope that will
    /// run the loop (see the class remarks). Same pattern as
    /// <c>ObserverService.TryAcquireAdvisoryLockAsync</c>, but per task rather than global.
    /// </summary>
    public async Task<bool> TryAcquireTaskLockAsync(Guid taskId, CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = AdvisoryLockKey(taskId);
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(ct);
        return result is true;
    }

    /// <summary>
    /// Releases every advisory lock held on this connection. <c>pg_advisory_unlock_all()</c> is used
    /// rather than the keyed form on purpose: the connection belongs to the run's own scope and holds
    /// exactly one lock, and this cannot accidentally release a DIFFERENT task's key if the key
    /// computation ever changes.
    /// </summary>
    public async Task ReleaseTaskLockAsync()
    {
        try
        {
            var connection = _db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                return;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock_all()";
            await command.ExecuteScalarAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The lock is released when the connection closes; never fail the run over this.
            _logger.LogWarning(ex, "Agent advisory lock release failed.");
        }
    }

    /// <summary>
    /// Deterministic 64-bit key for a task. <c>Guid.GetHashCode()</c> is explicitly NOT used: it is not
    /// stable across processes, which would let two instances lock different keys for the same task.
    /// </summary>
    public static long AdvisoryLockKey(Guid taskId) => BitConverter.ToInt64(taskId.ToByteArray(), 0);
}
