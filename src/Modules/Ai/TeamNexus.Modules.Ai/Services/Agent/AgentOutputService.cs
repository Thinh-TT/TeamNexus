using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>Outcome of turning an accepted draft into a Pending action (Phase 7 §4.8d).</summary>
/// <param name="LogId">The Pending <c>ai_action_logs</c> row, or <c>null</c> when creation failed.</param>
/// <param name="Kind"><see cref="AgentOutputKinds"/> value that was chosen.</param>
/// <param name="Error">Human-readable reason when nothing was created.</param>
public sealed record AgentOutputResult(Guid? LogId, string Kind, string? Error);

/// <summary>
/// Turns the draft accepted by <c>DraftOutput</c> into a Pending action on the Accountability Layer
/// (Phase 7 §4.8/D9): a short result becomes a <c>PostComment</c>, a long/formatted one a
/// <c>PostAttachment</c>. Writes nothing to business data — approval is what writes.
/// </summary>
public interface IAgentOutputService
{
    Task<AgentOutputResult> CreatePendingAsync(
        AgentRun run, AgentDraft draft, CancellationToken ct = default);
}

public sealed class AgentOutputService : IAgentOutputService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Length of the draft excerpt kept in <c>basis</c> (never the whole draft).</summary>
    public const int BasisExcerptChars = 300;

    private readonly IAiActionService _actions;
    private readonly AgentEffectiveOptions _options;
    private readonly ILogger<AgentOutputService> _logger;

    public AgentOutputService(
        IAiActionService actions,
        IOptions<AgentOptions> options,
        ILogger<AgentOutputService> logger)
    {
        _actions = actions;
        _options = options.Value.Effective;
        _logger = logger;
    }

    public async Task<AgentOutputResult> CreatePendingAsync(
        AgentRun run, AgentDraft draft, CancellationToken ct = default)
    {
        var kind = AgentAttachmentFactory.ChooseKind(
            draft.Content, draft.FileName, draft.ContentType, _options.AttachmentThresholdChars);

        string actionType;
        string afterSnapshot;

        if (kind == AgentOutputKinds.Attachment)
        {
            // The byte cap is on the CONTENT (what the applier will decode), not on the base64 length:
            // Vietnamese text is multi-byte UTF-8, so 384 000 characters can exceed 512 KB.
            var byteCount = Encoding.UTF8.GetByteCount(draft.Content);
            if (byteCount > _options.MaxAttachmentBytes)
            {
                _logger.LogWarning(
                    "Agent run {RunId}: draft vượt cap attachment ({Bytes} > {Max} bytes) — không tạo action.",
                    run.Id, byteCount, _options.MaxAttachmentBytes);

                return new AgentOutputResult(
                    null,
                    kind,
                    $"Kết quả là tệp đính kèm nhưng vượt giới hạn Agent:MaxAttachmentBytes "
                    + $"({byteCount} > {_options.MaxAttachmentBytes} bytes).");
            }

            actionType = AiActionTypes.PostAttachment;
            afterSnapshot = JsonSerializer.Serialize(new AgentAttachmentSnapshot(
                run.TaskId,
                draft.FileName ?? string.Empty,
                draft.ContentType,
                Convert.ToBase64String(Encoding.UTF8.GetBytes(draft.Content)),
                run.Id), Json);
        }
        else
        {
            actionType = AiActionTypes.PostComment;
            afterSnapshot = JsonSerializer.Serialize(
                new AgentCommentSnapshot(run.TaskId, draft.Content), Json);
        }

        var basis = JsonSerializer.Serialize(new
        {
            runId = run.Id,
            taskId = run.TaskId,
            boardId = run.BoardId,
            outputKind = kind,
            contentLength = draft.Content.Length,
            contentExcerpt = draft.Content.Length <= BasisExcerptChars
                ? draft.Content
                : draft.Content[..BasisExcerptChars],
            toolCallCount = run.ToolCallCount,
            llmCallCount = run.LlmCallCount,
            promptTokens = run.PromptTokens,
            completionTokens = run.CompletionTokens,
            requestedBy = "ai_agent",
        }, Json);

        var log = await _actions.RequestAgentOutputAsync(
            run.TaskId, run.AgentUserId, actionType, afterSnapshot, basis, ct);

        _logger.LogInformation(
            "Agent run {RunId}: output kind {Kind} → Pending action {LogId} (task {TaskId}).",
            run.Id, kind, log.Id, run.TaskId);

        return new AgentOutputResult(log.Id, kind, null);
    }
}
