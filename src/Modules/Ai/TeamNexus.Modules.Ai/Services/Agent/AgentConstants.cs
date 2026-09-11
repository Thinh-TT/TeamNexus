namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>
/// String constants shared by the whole AI Agent Executor (Phase 7 §4.1):
/// values written into <c>agent_runs.output_kind</c>, the fake-provider markers and the agent's
/// notification types.
/// </summary>
public static class AgentOutputKinds
{
    /// <summary>Short result → a task comment (D9).</summary>
    public const string Comment = "Comment";

    /// <summary>Long/formatted result → a <c>task_attachments</c> row (D9).</summary>
    public const string Attachment = "Attachment";
}

/// <summary>
/// Prompt markers. <see cref="ObserverPrompts"/> recognised the Observer by a marker on the first
/// line of the <b>user</b> prompt; the agent's marker sits on the first line of the
/// <b>system</b> prompt instead, because the agent prompt is long and starts with context, so a
/// marker buried in the user message would be much harder to read (Phase 7 §4.5).
/// </summary>
public static class AgentMarkers
{
    /// <summary>First line of the agent system prompt — how <c>FakeAiProvider</c> recognises this flow.</summary>
    public const string Executor = """{"agent":"executor"}""";

    /// <summary>
    /// Sentinel prefix for the offline provider's scenario selection (Phase 7 §4.5). Only ever
    /// present when <c>DeepSeek:ApiKey</c> is empty (dev/verify), and namespaced so a real task
    /// title cannot plausibly collide.
    /// </summary>
    public const string FakeSentinelPrefix = "FAKE:";

    /// <summary>Forces the clarification branch (verify group E).</summary>
    public const string FakeClarify = FakeSentinelPrefix + "CLARIFY";

    /// <summary>Forces a long draft that <c>ChooseKind</c> must classify as an attachment (group D).</summary>
    public const string FakeAttach = FakeSentinelPrefix + "ATTACH";

    /// <summary>Makes one provider round slow, so a 1-second <c>RunTimeoutSeconds</c> really times out (group F).</summary>
    public const string FakeSlow = FakeSentinelPrefix + "SLOW";

    /// <summary>Emits a tool name outside the whitelist (group C: "model gọi bậy").</summary>
    public const string FakeUnknownTool = FakeSentinelPrefix + "UNKNOWN";

    /// <summary>Emits a syntactically invalid <c>arguments</c> string (group C).</summary>
    public const string FakeBadArguments = FakeSentinelPrefix + "BADJSON";
}

/// <summary>
/// <c>notifications.type</c> values produced by the agent (Phase 7 §4.8d, DB design §3.8).
/// <para>
/// <b>Deliberately a separate vocabulary from <see cref="NotificationTypes"/>.</b>
/// <c>NotificationTypes.All</c> is the Observer's anti-hallucination whitelist
/// (<c>ObserverFindingValidator</c>): adding the agent's types there would let the Observer model
/// emit "AgentRunFailed" and pass validation, writing alerts about a pipeline it cannot see.
/// These types are written by our own code only, so they need no validation list.
/// </para>
/// </summary>
public static class AgentNotificationTypes
{
    /// <summary>Run stopped abnormally (any guardrail or provider/task failure).</summary>
    public const string RunFailed = "AgentRunFailed";

    /// <summary>The agent asked a question and needs the team lead to answer it.</summary>
    public const string AwaitingClarification = "AgentAwaitingClarification";

    /// <summary>A draft is waiting for human approval in the Accountability drawer.</summary>
    public const string OutputPending = "AgentOutputPending";
}
