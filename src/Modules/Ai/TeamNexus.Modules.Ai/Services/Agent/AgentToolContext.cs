using System.Text.Json;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>
/// Everything a tool is allowed to know for one run (Phase 7 §4.4, risk R7).
/// <para>
/// <b>Security invariant:</b> <see cref="WorkspaceId"/>/<see cref="BoardId"/>/<see cref="TaskId"/>
/// come from this object — built by the orchestrator from the database — and are <b>never</b> read
/// from the model's tool arguments. That is what makes reading across workspaces impossible even if
/// the model asks for another id.
/// </para>
/// </summary>
public sealed class AgentToolContext
{
    public AgentToolContext(
        Guid workspaceId,
        Guid boardId,
        Guid taskId,
        Guid agentUserId,
        Guid runId)
    {
        WorkspaceId = workspaceId;
        BoardId = boardId;
        TaskId = taskId;
        AgentUserId = agentUserId;
        RunId = runId;
    }

    public Guid WorkspaceId { get; }

    public Guid BoardId { get; }

    public Guid TaskId { get; }

    public Guid AgentUserId { get; }

    public Guid RunId { get; }

    /// <summary>Draft accepted by <c>DraftOutput</c> — set by the tool, consumed after the loop ends.</summary>
    public AgentDraft? Draft { get; set; }

    /// <summary>Question accepted by <c>RequestClarification</c> — set by the tool, posted after the loop ends.</summary>
    public string? ClarificationQuestion { get; set; }
}

/// <summary>
/// Result of a tool call: the text handed back to the model plus the loop-control signal.
/// <paramref name="IsError"/> is recorded in <c>agent_runs.tool_call_trace</c> so a Manager can see
/// which tool calls went wrong.
/// </summary>
public sealed record AgentToolOutcome(
    string ResultJson,
    bool StopLoop = false,
    AgentStopReason? StopReason = null,
    bool IsError = false);

/// <summary>A draft produced by the agent: short ⇒ comment, long/formatted ⇒ attachment (D9).</summary>
public sealed record AgentDraft(string Content, string? FileName, string? ContentType);

/// <summary>Contract of one whitelisted agent tool.</summary>
public interface IAgentTool
{
    /// <summary>Exact tool name advertised to the model (must match <see cref="AgentToolDefinitions"/>).</summary>
    string Name { get; }

    /// <summary>
    /// Runs the tool. The returned string is what the model sees; a domain problem must be expressed
    /// as an error object inside it rather than as a thrown exception (the registry still guards).
    /// </summary>
    Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext context, CancellationToken ct);
}
