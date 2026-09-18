namespace TeamNexus.Modules.Ai.Options;

/// <summary>
/// AI Task Chat configuration (section "AiChat" in appsettings / env vars), following the
/// <c>DigestOptions</c>/<c>AgentOptions</c> pattern: a plain POCO bound from configuration with no
/// hard validation at startup, so a missing section simply yields the documented defaults.
/// <para>
/// <b>Why its own section instead of reusing <c>DeepSeek:MaxTokens</c>:</b> the roadmap's Phase 14
/// guardrail ("giới hạn token áp dụng cho mọi tính năng mới") has to be <b>local</b> to each feature.
/// Sharing the Smart Setup knob would mean one later tweak to
/// <c>DeepSeek:MaxTokens</c> silently widening the chat's budget too — and the chat is the only feature
/// here that spends tokens on every keystroke-sized question.
/// </para>
/// </summary>
public sealed class AiChatOptions
{
    public const string SectionName = "AiChat";

    /// <summary>
    /// Master switch. <c>false</c> ⇒ the endpoint answers <b>503</b> before touching the database or
    /// the provider (precedent: <c>AgentDisabledException</c>).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How many past messages the client may send back. The transcript lives in the browser (the server
    /// keeps no chat table), so this cap is the guardrail that bounds prompt cost.
    /// Clamped to 2..50 by <see cref="Effective"/>.
    /// </summary>
    public int MaxHistoryMessages { get; set; } = 12;

    /// <summary>
    /// Total characters allowed across all history messages (~2000 tokens for mixed Vietnamese/English).
    /// Exceeding it is a <b>400 before any provider call</b>. Clamped to 500..32000.
    /// </summary>
    public int MaxHistoryChars { get; set; } = 8000;

    /// <summary>Output token cap for the streamed answer. Clamped to 64..8192.</summary>
    public int MaxOutputTokens { get; set; } = 1200;

    /// <summary>
    /// Higher than the Observer's 0 (which must not be creative): a chat answer should read naturally.
    /// Clamped to 0..2.
    /// </summary>
    public double Temperature { get; set; } = 0.3;

    /// <summary>Most recent task comments folded into the context block. Clamped to 0..50.</summary>
    public int MaxCommentsInContext { get; set; } = 10;

    /// <summary>Per-comment truncation so one long comment cannot dominate the prompt. Clamped to 50..4000.</summary>
    public int MaxCommentCharsInContext { get; set; } = 500;

    /// <summary>Total characters of the whole task context block. Clamped to 500..32000.</summary>
    public int MaxTaskContextChars { get; set; } = 6000;

    /// <summary>
    /// Longest answer a user may save as a comment (<c>task_comments.content</c> is capped at 2000, and
    /// the UI disables its button past this). Clamped to 1..2000 — never above the column limit.
    /// </summary>
    public int MaxAnswerChars { get; set; } = 2000;

    /// <summary>Clamped view of the options; every consumer reads this, never the raw properties.</summary>
    public AiChatEffectiveOptions Effective => new(
        MaxHistoryMessages: Math.Clamp(MaxHistoryMessages, 2, 50),
        MaxHistoryChars: Math.Clamp(MaxHistoryChars, 500, 32_000),
        MaxOutputTokens: Math.Clamp(MaxOutputTokens, 64, 8192),
        Temperature: Math.Clamp(Temperature, 0, 2),
        MaxCommentsInContext: Math.Clamp(MaxCommentsInContext, 0, 50),
        MaxCommentCharsInContext: Math.Clamp(MaxCommentCharsInContext, 50, 4000),
        MaxTaskContextChars: Math.Clamp(MaxTaskContextChars, 500, 32_000),
        MaxAnswerChars: Math.Clamp(MaxAnswerChars, 1, 2000));
}

/// <summary>
/// The clamped values the chat service and its endpoint actually use. A plain record so a test can
/// build one inline — the same shape as <c>AgentEffectiveOptions</c>.
/// </summary>
public sealed record AiChatEffectiveOptions(
    int MaxHistoryMessages,
    int MaxHistoryChars,
    int MaxOutputTokens,
    double Temperature,
    int MaxCommentsInContext,
    int MaxCommentCharsInContext,
    int MaxTaskContextChars,
    int MaxAnswerChars);
