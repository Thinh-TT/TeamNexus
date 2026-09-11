using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>Dispatches a model tool call to the whitelisted implementation (Phase 7 §4.4).</summary>
public interface IAgentToolRegistry
{
    /// <summary>Tools advertised to the model (and validated against on dispatch).</summary>
    IReadOnlyList<AiToolDefinition> Definitions { get; }

    /// <summary>
    /// Runs one tool call. Never throws for a *tool* problem: an unknown tool, invalid
    /// <c>arguments</c> JSON or a failing tool all come back as <c>{"error": …}</c> for the model to
    /// act on. Only caller cancellation propagates.
    /// </summary>
    Task<AgentToolOutcome> DispatchAsync(
        string name, string? argumentsJson, AgentToolContext context, CancellationToken ct = default);
}

/// <summary>
/// Whitelist dispatcher. The name is matched case-insensitively against
/// <see cref="AgentToolDefinitions.All"/>; anything else is refused, which is the "model gọi bậy"
/// scenario the verification run must be able to provoke.
/// </summary>
public sealed class AgentToolRegistry : IAgentToolRegistry
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IReadOnlyDictionary<string, IAgentTool> _tools;
    private readonly ILogger<AgentToolRegistry> _logger;

    public AgentToolRegistry(IEnumerable<IAgentTool> tools, ILogger<AgentToolRegistry> logger)
    {
        _logger = logger;
        _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<AiToolDefinition> Definitions => AgentToolDefinitions.All;

    public async Task<AgentToolOutcome> DispatchAsync(
        string name, string? argumentsJson, AgentToolContext context, CancellationToken ct = default)
    {
        if (!AgentToolDefinitions.IsWhitelisted(name) || !_tools.TryGetValue(name, out var tool))
        {
            _logger.LogWarning("Agent run {RunId}: model gọi tool ngoài whitelist '{Name}'.", context.RunId, name);

            return new AgentToolOutcome(Error($"Unknown tool '{name}'."), IsError: true);
        }

        if (!TryParseArguments(argumentsJson, out var arguments, out var parseError))
        {
            // Not counted as a successful tool call semantics-wise, but the orchestrator still
            // increments tool_call_count — the model got a round back, so it cost a round.
            _logger.LogWarning(
                "Agent run {RunId}: arguments của tool {Name} không parse được: {Error}",
                context.RunId,
                name,
                parseError);

            return new AgentToolOutcome(Error($"Invalid JSON arguments: {parseError}"), IsError: true);
        }

        var stopwatch = Stopwatch.StartNew();
        string result;
        var isError = false;

        try
        {
            result = await tool.ExecuteAsync(arguments, context, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Real cancellation must reach the orchestrator (cancel endpoint / wall-clock timeout).
            throw;
        }
        catch (Exception ex)
        {
            // A tool failure is the model's problem to route around, never a run failure.
            _logger.LogWarning(ex, "Agent run {RunId}: tool {Name} lỗi.", context.RunId, name);
            result = Error(ex.Message);
            isError = true;
        }

        stopwatch.Stop();

        // Loop control: only a tool that actually accepted something may stop the loop, so an
        // over-long draft (rejected with an error) leaves the loop running for a retry.
        return name switch
        {
            var n when string.Equals(n, AgentToolDefinitions.DraftOutputName, StringComparison.OrdinalIgnoreCase)
                       && context.Draft is not null
                => new AgentToolOutcome(result, StopLoop: true, StopReason: AgentStopReason.DraftProduced, IsError: isError),

            var n when string.Equals(n, AgentToolDefinitions.RequestClarificationName, StringComparison.OrdinalIgnoreCase)
                       && context.ClarificationQuestion is not null
                => new AgentToolOutcome(result, StopLoop: true, StopReason: AgentStopReason.QuestionAsked, IsError: isError),

            _ => new AgentToolOutcome(result, StopLoop: false, StopReason: null, IsError: isError),
        };
    }

    /// <summary>jsonb-safe error object handed back to the model.</summary>
    public static string Error(string message) => JsonSerializer.Serialize(new { error = message }, Json);

    /// <summary>
    /// Parses the (untrusted) arguments string. A bare JSON value is accepted; a malformed string is
    /// reported to the model instead of aborting the run.
    /// </summary>
    private static bool TryParseArguments(string? argumentsJson, out JsonElement arguments, out string? error)
    {
        arguments = default;
        error = null;

        var json = argumentsJson?.Trim();
        if (string.IsNullOrEmpty(json))
        {
            // No-argument call: behave as an empty object.
            arguments = JsonSerializer.Deserialize<JsonElement>("{}");
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            arguments = document.RootElement.Clone();

            if (arguments.ValueKind != JsonValueKind.Object)
            {
                error = "arguments must be a JSON object";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
