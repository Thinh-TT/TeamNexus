using System.Text.Json;

namespace TeamNexus.Modules.Ai.Services.Agent.Agents;

/// <summary>
/// <c>RequestClarification</c> (Phase 7 §4.4): records the question the agent wants the team lead to
/// answer.
/// <para>
/// <b>Does not write the comment itself.</b> The orchestrator posts it after the loop (so exactly one
/// place writes to <c>task_comments</c>) and it deliberately does <b>not</b> go through the
/// Accountability Layer: a question is not a business-data write (D10).
/// </para>
/// </summary>
public sealed class RequestClarificationTool : IAgentTool
{
    /// <summary>Matches the <c>agent_runs.clarification_question</c> column limit.</summary>
    public const int MaxQuestionLength = 2000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => AgentToolDefinitions.RequestClarificationName;

    public Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var question = ToolArguments.GetString(arguments, "question")?.Trim();
        if (string.IsNullOrEmpty(question))
        {
            return Task.FromResult(AgentToolRegistry.Error("'question' is required and must not be empty."));
        }

        if (context.ClarificationQuestion is not null)
        {
            return Task.FromResult(AgentToolRegistry.Error("A clarification question was already asked for this run."));
        }

        if (question.Length > MaxQuestionLength)
        {
            return Task.FromResult(AgentToolRegistry.Error(
                $"question is too long ({question.Length} chars, limit {MaxQuestionLength}); shorten it."));
        }

        context.ClarificationQuestion = question;

        return Task.FromResult(JsonSerializer.Serialize(new { accepted = true }, Json));
    }
}
