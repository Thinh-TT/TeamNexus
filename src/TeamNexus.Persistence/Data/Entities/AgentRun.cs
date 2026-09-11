namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Lifecycle of one AI Agent run (Phase 7 §2.5 / DB design §3.8). Stored as text + CHECK
/// constraint <c>ck_agent_runs_status</c>.
/// <para>
/// Deliberately separate from <see cref="AgentStopReason"/> (D4): the status answers "where did the
/// run end up", the stop reason answers "why". A budget overrun is therefore NOT a status — it is
/// <see cref="AgentStopReason.ToolLimit"/><c>/</c><see cref="AgentStopReason.TimeLimit"/><c>/</c>
/// <see cref="AgentStopReason.TokenBudget"/> on <c>Status = Failed</c>, following the
/// <see cref="ObserverRunStatus.Skipped"/> precedent ("not an error" is a separate concept).
/// </para>
/// </summary>
public enum AgentRunStatus
{
    /// <summary>Tool-calling loop in progress (or crashed mid-run — the reaper closes it).</summary>
    Running,

    /// <summary>Agent asked a clarification question and is waiting for a human answer.</summary>
    AwaitingClarification,

    /// <summary>Agent produced a draft; an <c>ai_action_logs</c> row is Pending human approval.</summary>
    AwaitingApproval,

    /// <summary>Run finished and its output went through the Accountability Layer.</summary>
    Completed,

    /// <summary>Run stopped abnormally — see <see cref="AgentRun.StopReason"/> for the cause.</summary>
    Failed,
}

/// <summary>
/// Why a run stopped (Phase 7 §2.5 / DB design §3.8). Stored as text + CHECK
/// <c>ck_agent_runs_stop_reason</c>; null while the run is <see cref="AgentRunStatus.Running"/>.
/// <para>
/// There is intentionally no <c>BudgetExceeded</c> member — see <see cref="AgentRunStatus"/>.
/// </para>
/// </summary>
public enum AgentStopReason
{
    /// <summary>Agent called <c>DraftOutput</c>; the result is waiting for human approval.</summary>
    DraftProduced,

    /// <summary>Agent called <c>RequestClarification</c>; the task moved to the clarification column.</summary>
    QuestionAsked,

    /// <summary>Guardrail: more tool calls than <c>Agent:MaxToolCalls</c>.</summary>
    ToolLimit,

    /// <summary>Guardrail: wall-clock longer than <c>Agent:RunTimeoutSeconds</c>.</summary>
    TimeLimit,

    /// <summary>Guardrail: prompt + completion tokens above <c>Agent:MaxRunTokens</c>.</summary>
    TokenBudget,

    /// <summary>The AI provider failed (HTTP error, timeout, unparsable payload).</summary>
    ProviderError,

    /// <summary>Human cancelled the run through <c>POST /api/agent-runs/{id}/cancel</c>.</summary>
    Cancelled,

    /// <summary>The task changed mid-run (deleted, reassigned, moved out of the clarification column).</summary>
    TaskChanged,

    /// <summary>Anything else — including the orphan-run reaper closing an abandoned <c>Running</c> row.</summary>
    InternalError,
}

/// <summary>
/// Progress journal of ONE AI Agent execution of ONE task (Phase 7 §2.3 / DB design §3.8).
/// Table: <c>agent_runs</c>.
/// <para>
/// Why this is not <see cref="AiActionLog"/>: that table is the <b>decision log</b> for data writes
/// (Pending → Approved/Rejected/Undone), while this table is the <b>run log</b> (tool-call loop,
/// token usage, why it stopped). They are linked by <see cref="AiActionLogId"/>.
/// </para>
/// <para>
/// Append-only per run (D14): "Chạy lại" inserts a NEW row pointing at the previous one through
/// <see cref="PreviousRunId"/>. An old row is never edited. There is no query filter and no soft
/// delete — same invariant as <see cref="AiActionLog"/>.
/// </para>
/// <para>
/// No navigation properties on purpose: navigating to the task would drag the task's
/// <c>deleted_at</c> query filter into run lookups and hide runs of soft-deleted tasks.
/// </para>
/// </summary>
public class AgentRun : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Workspace of the task — used for audit and (optional) retention pruning.</summary>
    public Guid WorkspaceId { get; set; }

    /// <summary>Board of the task — the SignalR group the progress events are broadcast to.</summary>
    public Guid BoardId { get; set; }

    /// <summary>The task this run executes.</summary>
    public Guid TaskId { get; set; }

    /// <summary>The workspace's AI Agent user row (<c>member_type = 'ai_agent'</c>).</summary>
    public Guid AgentUserId { get; set; }

    /// <summary>The Manager/Admin who pressed "Chạy Agent" / "Chạy lại".</summary>
    public Guid TriggeredByUserId { get; set; }

    public AgentRunStatus Status { get; set; } = AgentRunStatus.Running;

    /// <summary>Null while the run has not stopped yet.</summary>
    public AgentStopReason? StopReason { get; set; }

    /// <summary>Clarification question asked by the agent (≤ 2000).</summary>
    public string? ClarificationQuestion { get; set; }

    /// <summary>Comment in which the agent posted <see cref="ClarificationQuestion"/>.</summary>
    public Guid? ClarificationCommentId { get; set; }

    /// <summary>Human answer used by the "Chạy lại" run that continued this one.</summary>
    public Guid? ResolutionCommentId { get; set; }

    /// <summary>Self reference: the run this one re-runs (append-only chain, D14).</summary>
    public Guid? PreviousRunId { get; set; }

    /// <summary>
    /// jsonb — compact array of <c>{ name, arguments, resultSummary, isError, at, durationMs }</c>
    /// entries, capped by <c>Agent:ToolTraceMaxEntries</c> / <c>Agent:ToolTraceResultChars</c>.
    /// Kept as raw JSON text (Npgsql string↔jsonb mapping), serialized explicitly in the Ai module.
    /// </summary>
    public string ToolCallTrace { get; set; } = "[]";

    /// <summary>True when the trace was cut at <c>Agent:ToolTraceMaxEntries</c>.</summary>
    public bool TraceTruncated { get; set; }

    public int ToolCallCount { get; set; }

    public int LlmCallCount { get; set; }

    /// <summary>Prompt tokens summed over EVERY provider call of this run.</summary>
    public int PromptTokens { get; set; }

    /// <summary>Completion tokens summed over every provider call of this run.</summary>
    public int CompletionTokens { get; set; }

    /// <summary><c>Comment</c> | <c>Attachment</c> (see <c>AgentOutputKinds</c>); null when no output.</summary>
    public string? OutputKind { get; set; }

    /// <summary>The Pending <see cref="AiActionLog"/> that carries the produced output.</summary>
    public Guid? AiActionLogId { get; set; }

    /// <summary>True once the Manager was warned about an abnormal stop (anti-spam flag).</summary>
    public bool NotificationSent { get; set; }

    /// <summary>Failure detail / guardrail numbers (≤ 2000).</summary>
    public string? Error { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Null while the run has not stopped yet.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
