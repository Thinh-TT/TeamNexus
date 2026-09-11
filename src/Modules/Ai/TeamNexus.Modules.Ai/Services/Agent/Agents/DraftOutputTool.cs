using System.Text.Json;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;

namespace TeamNexus.Modules.Ai.Services.Agent.Agents;

/// <summary>
/// <c>DraftOutput</c> (Phase 7 §4.4): accepts the agent's final result and classifies it as a comment
/// or an attachment.
/// <para>
/// <b>Writes nothing to the database.</b> It only records the draft on
/// <see cref="AgentToolContext.Draft"/>; persisting happens after the loop ends, through the
/// Accountability Layer (<c>AgentOutputService</c>) — so a rejected draft leaves no trace at all.
/// </para>
/// </summary>
public sealed class DraftOutputTool : IAgentTool
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AgentEffectiveOptions _options;

    public DraftOutputTool(IOptions<AgentOptions> options)
    {
        _options = options.Value.Effective;
    }

    public string Name => AgentToolDefinitions.DraftOutputName;

    public Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var content = ToolArguments.GetString(arguments, "content");
        if (string.IsNullOrWhiteSpace(content))
        {
            return Task.FromResult(AgentToolRegistry.Error("'content' is required and must not be empty."));
        }

        if (context.Draft is not null)
        {
            return Task.FromResult(AgentToolRegistry.Error("A draft was already accepted for this run."));
        }

        if (content.Length > _options.MaxDraftChars)
        {
            // Returned as an error so the model can shorten it and call the tool again, instead of
            // failing the whole run over a size limit.
            return Task.FromResult(AgentToolRegistry.Error(
                $"content is too long ({content.Length} chars, limit {_options.MaxDraftChars}); "
                + "shorten it and call DraftOutput again."));
        }

        var fileName = ToolArguments.GetTrimmedString(arguments, "fileName");
        var contentType = ToolArguments.GetTrimmedString(arguments, "contentType");
        var kind = AgentAttachmentFactory.ChooseKind(
            content, fileName, contentType, _options.AttachmentThresholdChars);

        // An explicit fileName only makes sense for a file; a stray name on a short comment would
        // otherwise create an attachment the caller did not ask for.
        if (kind == AgentOutputKinds.Comment)
        {
            fileName = null;
            contentType = null;
        }

        context.Draft = new AgentDraft(content, fileName, contentType);

        return Task.FromResult(JsonSerializer.Serialize(new { accepted = true, kind }, Json));
    }
}
