namespace TeamNexus.Modules.Ai.Options;

/// <summary>
/// AI Agent Chat configuration (section "AgentChat" in appsettings / env vars) — the board-scoped
/// chat that replaces the task-scoped limits of <see cref="AiChatOptions"/>.
/// <para>
/// <b>Why a separate section instead of widening <c>AiChat</c>.</b> The board chat deliberately carries
/// 2–3× the context: up to <see cref="MaxAttachedTasks"/> tasks, each with its own comments, on top of a
/// longer conversation. Putting those numbers in <c>AiChat</c> would silently loosen the guardrail of
/// the task chat too — the exact hazard Phase 14's D9 called out when it refused to share
/// <c>DeepSeek:MaxTokens</c>. Two features, two budgets, both clamped.
/// </para>
/// <para>
/// <b>Every property is clamped by <see cref="Effective"/>.</b> A configuration of <c>0</c>, a negative
/// number or an absurd one must never be able to switch the cost ceiling off.
/// </para>
/// </summary>
public sealed class AgentChatOptions
{
    public const string SectionName = "AgentChat";

    /// <summary>
    /// Master switch. <c>false</c> ⇒ the endpoints answer <b>503</b> before touching the database or the
    /// provider (precedent: <c>AgentDisabledException</c>/<c>AiChatDisabledException</c>).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How many past messages the client may send back. Twice the task chat's 12 because a board
    /// conversation covers several tasks. Clamped to 2..60.
    /// </summary>
    public int MaxHistoryMessages { get; set; } = 24;

    /// <summary>
    /// Total characters across all history messages (~6000 tokens for mixed Vietnamese/English).
    /// Exceeding it is a <b>400 before any provider call</b>. Clamped to 500..64000.
    /// </summary>
    public int MaxHistoryChars { get; set; } = 24_000;

    /// <summary>
    /// Output token cap for the streamed answer. Doubled from the task chat's 1200 because a board
    /// answer can be a plan rather than a paragraph. Clamped to 64..8192.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 2400;

    /// <summary>Clamped to 0..2 (a chat answer should read naturally).</summary>
    public double Temperature { get; set; } = 0.3;

    /// <summary>
    /// Most tasks a single question may attach. The board UI caps itself at the same number so a user
    /// cannot build a request the server will reject. Clamped to 1..20.
    /// </summary>
    public int MaxAttachedTasks { get; set; } = 8;

    /// <summary>Per-task description/context truncation. Clamped to 200..8000.</summary>
    public int MaxTaskContextChars { get; set; } = 3000;

    /// <summary>
    /// Total characters of the whole attached-task context block, across every attached task plus the
    /// board description. Clamped to 1000..48000 — this, not <see cref="MaxAttachedTasks"/>, is the
    /// number that actually bounds the prompt.
    /// </summary>
    public int MaxTotalContextChars { get; set; } = 24_000;

    /// <summary>Most recent comments folded in, <b>per attached task</b>. Clamped to 0..50.</summary>
    public int MaxCommentsInContext { get; set; } = 6;

    /// <summary>Per-comment truncation. Clamped to 50..4000.</summary>
    public int MaxCommentCharsInContext { get; set; } = 400;

    /// <summary>
    /// Longest single turn stored in <c>agent_chat_messages.content</c>. Much larger than
    /// <see cref="MaxAnswerChars"/> because this is history, not a posted comment. Clamped 1..16000.
    /// </summary>
    public int MaxMessageChars { get; set; } = 16_000;

    /// <summary>
    /// Longest answer a user may save as a task comment. <b>Deliberately not widened:</b>
    /// <c>task_comments.content</c> is capped at 2000, so a larger value here would only trade a clear
    /// 400 for a silent database truncation. Clamped 1..2000.
    /// </summary>
    public int MaxAnswerChars { get; set; } = 2000;

    /// <summary>
    /// How many write proposals one answer may carry. Held at <b>1</b> and clamped to exactly 1: a model
    /// that batches "rename this, move that and create five tasks" into one answer is asking a Manager
    /// to approve a bundle they cannot judge part by part.
    /// </summary>
    public int MaxProposalsPerAnswer { get; set; } = 1;

    /// <summary>Days a chat turn is kept before the retention sweep may delete it. Clamped 1..3650.</summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>Rows deleted per retention batch, so a first sweep never locks the table. Clamped 100..20000.</summary>
    public int RetentionBatchSize { get; set; } = 2000;

    /// <summary>Clamped view of the options; every consumer reads this, never the raw properties.</summary>
    public AgentChatEffectiveOptions Effective => new(
        MaxHistoryMessages: Math.Clamp(MaxHistoryMessages, 2, 60),
        MaxHistoryChars: Math.Clamp(MaxHistoryChars, 500, 64_000),
        MaxOutputTokens: Math.Clamp(MaxOutputTokens, 64, 8192),
        Temperature: Math.Clamp(Temperature, 0, 2),
        MaxAttachedTasks: Math.Clamp(MaxAttachedTasks, 1, 20),
        MaxTaskContextChars: Math.Clamp(MaxTaskContextChars, 200, 8000),
        MaxTotalContextChars: Math.Clamp(MaxTotalContextChars, 1000, 48_000),
        MaxCommentsInContext: Math.Clamp(MaxCommentsInContext, 0, 50),
        MaxCommentCharsInContext: Math.Clamp(MaxCommentCharsInContext, 50, 4000),
        MaxMessageChars: Math.Clamp(MaxMessageChars, 1, 16_000),
        MaxAnswerChars: Math.Clamp(MaxAnswerChars, 1, 2000),
        // `Math.Clamp(_, 1, 1)` reads oddly but states the intent: the knob exists so a future phase can
        // raise it deliberately, and no configuration can raise it today.
        MaxProposalsPerAnswer: Math.Clamp(MaxProposalsPerAnswer, 1, 1),
        RetentionDays: Math.Clamp(RetentionDays, 1, 3650),
        RetentionBatchSize: Math.Clamp(RetentionBatchSize, 100, 20_000));
}

/// <summary>
/// The clamped values the Agent Chat service and its endpoint actually use. A plain record so a test can
/// build one inline — the same shape as <c>AiChatEffectiveOptions</c>/<c>AgentEffectiveOptions</c>.
/// </summary>
public sealed record AgentChatEffectiveOptions(
    int MaxHistoryMessages,
    int MaxHistoryChars,
    int MaxOutputTokens,
    double Temperature,
    int MaxAttachedTasks,
    int MaxTaskContextChars,
    int MaxTotalContextChars,
    int MaxCommentsInContext,
    int MaxCommentCharsInContext,
    int MaxMessageChars,
    int MaxAnswerChars,
    int MaxProposalsPerAnswer,
    int RetentionDays,
    int RetentionBatchSize);
