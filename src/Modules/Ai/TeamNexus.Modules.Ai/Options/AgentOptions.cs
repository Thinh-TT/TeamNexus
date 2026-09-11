namespace TeamNexus.Modules.Ai.Options;

/// <summary>
/// AI Agent Executor configuration (section "Agent" in appsettings / env vars), following the
/// <c>DeepSeekOptions</c>/<c>ObserverOptions</c> pattern: a plain POCO bound from configuration,
/// no <c>ValidateOnStart</c>, and every threshold is defensively clamped through
/// <see cref="Effective"/> so a misconfigured 0 or negative value can never disable a guardrail.
/// <para>
/// Phase 7 §0 D18: every threshold/cap lives here — never hard-coded — so demo tuning needs no
/// rebuild and <c>Enabled = false</c> is the safe production off-switch (503, precedent
/// <c>Reports:Enabled</c>).
/// </para>
/// </summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Master switch for the three WRITE routes. <c>false</c> ⇒ 503 (reads keep working).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Display name of the workspace's AI Agent pseudo-member (D1).</summary>
    public string AgentDisplayName { get; set; } = "TeamNexus Agent";

    /// <summary>Optional avatar for the agent member shown in the assignee dropdown.</summary>
    public string? AgentAvatarUrl { get; set; }

    /// <summary>
    /// Name of the lazily created "Chờ làm rõ" column (D3). Added beyond the original §6 table so
    /// the Vietnamese label is not hard-coded in the resolver.
    /// </summary>
    public string ClarificationColumnName { get; set; } = "Chờ làm rõ";

    // ---- guardrails (D12 — checked after EVERY loop turn) ----------------------

    /// <summary>Hard cap on dispatched tool calls per run.</summary>
    public int MaxToolCalls { get; set; } = 15;

    /// <summary>Wall-clock cap for one run, enforced through <c>CancellationTokenSource.CancelAfter</c>.</summary>
    public int RunTimeoutSeconds { get; set; } = 300;

    /// <summary>Cap on prompt + completion tokens summed over every provider call of a run.</summary>
    public int MaxRunTokens { get; set; } = 50_000;

    /// <summary>Cap on provider round-trips — the anti-infinite-loop guard (there is no stop reason for it).</summary>
    public int MaxRunLlmCalls { get; set; } = 20;

    // ---- prompt / tool-result sizing (token control) ---------------------------

    /// <summary>Max characters of ONE tool result handed back to the model.</summary>
    public int MaxToolResultChars { get; set; } = 4_000;

    /// <summary>When the summed tool results exceed this, the oldest are replaced by a placeholder.</summary>
    public int MaxTotalToolResultChars { get; set; } = 40_000;

    /// <summary>Max characters of the composed task context (comments included).</summary>
    public int MaxTaskContextChars { get; set; } = 8_000;

    /// <summary>How many task comments are sent to the model.</summary>
    public int MaxCommentsInContext { get; set; } = 20;

    /// <summary>Per-comment cut applied before summing the context.</summary>
    public int MaxCommentCharsInContext { get; set; } = 1_000;

    /// <summary>
    /// Max characters of a <c>DraftOutput.content</c>. 384 000 ASCII → ~512 000 base64 chars, which
    /// keeps the <c>ai_action_logs.after_snapshot</c> jsonb row at roughly 512 KB (×1.37 blow-up is
    /// the documented reason this cap exists at all — Phase 7 §4.8/§7B).
    /// </summary>
    public int MaxDraftChars { get; set; } = 384_000;

    /// <summary>Draft longer than this becomes an attachment instead of a comment.</summary>
    public int AttachmentThresholdChars { get; set; } = 2_000;

    /// <summary>512 KB — the hard byte cap of <c>task_attachments.content</c> (D5).</summary>
    public int MaxAttachmentBytes { get; set; } = 524_288;

    /// <summary>Content type used when the model does not supply a usable one.</summary>
    public string DefaultAttachmentContentType { get; set; } = "text/markdown";

    /// <summary>Max entries kept in <c>agent_runs.tool_call_trace</c>.</summary>
    public int ToolTraceMaxEntries { get; set; } = 30;

    /// <summary>Max characters of one trace entry's result summary.</summary>
    public int ToolTraceResultChars { get; set; } = 500;

    /// <summary>Grace added to <see cref="RunTimeoutSeconds"/> before the reaper closes an orphan run (D13).</summary>
    public int OrphanRunGraceSeconds { get; set; } = 60;

    /// <summary>Retention of old <c>agent_runs</c> rows (D20 — optional, not implemented in this phase).</summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>Upper bound on `WebSearch.maxResults` (and on what the Tavily provider will ask for).</summary>
    public int WebSearchMaxResults { get; set; } = 5;

    // ---- derived / clamped -----------------------------------------------------

    /// <summary>
    /// Snapshot of every threshold with defensive clamping. Guardrail evaluation and every cap
    /// consumer read from here, never from the raw properties.
    /// </summary>
    public AgentEffectiveOptions Effective => new(
        MaxToolCalls: Math.Max(1, MaxToolCalls),
        RunTimeoutSeconds: Math.Max(1, RunTimeoutSeconds),
        MaxRunTokens: Math.Max(1, MaxRunTokens),
        MaxRunLlmCalls: Math.Max(1, MaxRunLlmCalls),
        MaxToolResultChars: Math.Max(200, MaxToolResultChars),
        MaxTotalToolResultChars: Math.Max(200, MaxTotalToolResultChars),
        MaxTaskContextChars: Math.Max(500, MaxTaskContextChars),
        MaxCommentsInContext: Math.Max(0, MaxCommentsInContext),
        MaxCommentCharsInContext: Math.Max(50, MaxCommentCharsInContext),
        MaxDraftChars: Math.Max(1, MaxDraftChars),
        AttachmentThresholdChars: Math.Max(1, AttachmentThresholdChars),
        MaxAttachmentBytes: Math.Max(1, MaxAttachmentBytes),
        ToolTraceMaxEntries: Math.Max(1, ToolTraceMaxEntries),
        ToolTraceResultChars: Math.Max(50, ToolTraceResultChars),
        OrphanRunGraceSeconds: Math.Max(0, OrphanRunGraceSeconds),
        WebSearchMaxResults: Math.Clamp(WebSearchMaxResults, 1, 10),
        DefaultAttachmentContentType: string.IsNullOrWhiteSpace(DefaultAttachmentContentType)
            ? "text/markdown"
            : DefaultAttachmentContentType.Trim());

    public TimeSpan RunTimeout => TimeSpan.FromSeconds(Effective.RunTimeoutSeconds);

    public TimeSpan OrphanCutoff => RunTimeout + TimeSpan.FromSeconds(Effective.OrphanRunGraceSeconds);

    public string EffectiveAgentDisplayName =>
        string.IsNullOrWhiteSpace(AgentDisplayName) ? "TeamNexus Agent" : AgentDisplayName.Trim();

    public string EffectiveClarificationColumnName =>
        string.IsNullOrWhiteSpace(ClarificationColumnName) ? "Chờ làm rõ" : ClarificationColumnName.Trim();
}

/// <summary>
/// Immutable, clamped view of <see cref="AgentOptions"/>. Passed to the pure guardrail/attachment
/// helpers so those stay free of configuration plumbing (and testable with a literal).
/// </summary>
public sealed record AgentEffectiveOptions(
    int MaxToolCalls,
    int RunTimeoutSeconds,
    int MaxRunTokens,
    int MaxRunLlmCalls,
    int MaxToolResultChars,
    int MaxTotalToolResultChars,
    int MaxTaskContextChars,
    int MaxCommentsInContext,
    int MaxCommentCharsInContext,
    int MaxDraftChars,
    int AttachmentThresholdChars,
    int MaxAttachmentBytes,
    int ToolTraceMaxEntries,
    int ToolTraceResultChars,
    int OrphanRunGraceSeconds,
    int WebSearchMaxResults,
    string DefaultAttachmentContentType);
