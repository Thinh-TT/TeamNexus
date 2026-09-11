using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Ai.Services.Agent;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Appliers;

/// <summary>
/// Applier for <see cref="AiActionTypes.PostAttachment"/> (Phase 7 §4.7): stores the agent's long
/// result as a <c>task_attachments</c> row (<c>bytea</c>, cap 512 KB).
/// <para>
/// Undo is a <b>hard delete</b> — this table is the single exception to soft delete in the schema
/// (D5): leftover <c>bytea</c> burns real quota and Undo must return the workspace to exactly its
/// previous state. The accepted consequence is that an undone file cannot be restored.
/// </para>
/// </summary>
public sealed class PostAttachmentApplier : IAiActionApplier
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TeamNexusDbContext _db;
    private readonly AgentEffectiveOptions _options;
    private readonly ILogger<PostAttachmentApplier> _logger;

    public PostAttachmentApplier(
        TeamNexusDbContext db,
        IOptions<AgentOptions> options,
        ILogger<PostAttachmentApplier> logger)
    {
        _db = db;
        _options = options.Value.Effective;
        _logger = logger;
    }

    public string ActionType => AiActionTypes.PostAttachment;

    public async Task<AiActionAppliedResult> ApplyAsync(
        AiActionLog log, AiActionContext ctx, CancellationToken ct = default)
    {
        var snapshot = Parse(log);

        byte[] content;
        try
        {
            content = Convert.FromBase64String(snapshot.ContentBase64);
        }
        catch (FormatException ex)
        {
            throw new BadRequestException($"The attachment payload is not valid base64: {ex.Message}");
        }

        // Second line of defence after AgentOutputService: the byte cap is measured on the DECODED
        // body, which is what actually lands in the database. Rejecting here leaves the log Pending
        // (the approve transaction rolls back), so the manager can reject it instead.
        if (content.Length > _options.MaxAttachmentBytes)
        {
            throw new BadRequestException(
                $"The attachment is too large ({content.Length} bytes, limit {_options.MaxAttachmentBytes}).");
        }

        var fileName = AgentAttachmentFactory.SafeFileName(
            snapshot.FileName, snapshot.ContentType, _options.DefaultAttachmentContentType);

        var contentType = AgentAttachmentFactory.NormalizeContentType(
            snapshot.ContentType, _options.DefaultAttachmentContentType);

        var attachment = new TaskAttachment
        {
            Id = Guid.NewGuid(),
            TaskId = snapshot.TaskId,
            // The AGENT created the file, not the manager who approved it.
            CreatedByUserId = log.RequestedByUserId,
            SourceRunId = snapshot.SourceRunId,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = content.Length,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.TaskAttachments.Add(attachment);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "PostAttachment applied (log {LogId}, task {TaskId}, attachment {AttachmentId}, {Bytes} bytes, '{FileName}').",
            log.Id, snapshot.TaskId, attachment.Id, attachment.SizeBytes, attachment.FileName);

        return new AiActionAppliedResult(
            AiEntityTypes.Task,
            snapshot.TaskId,
            [],
            [],
            [],
            CreatedAttachmentId: attachment.Id);
    }

    public async Task<IReadOnlyList<string>> UndoAsync(
        AiActionLog log, AiActionContext ctx, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var attachmentId = AiActionService.ReadGuid(log.AppliedSnapshot, "createdAttachmentId");

        if (attachmentId is null)
        {
            warnings.Add("The action has no createdAttachmentId; nothing to remove.");
            return warnings;
        }

        // IgnoreQueryFilters: TaskAttachment has no soft delete and no filter, but reading through the
        // tracked set makes the intent explicit and survives a future filter being added.
        var attachment = await _db.TaskAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId.Value, ct);

        if (attachment is null)
        {
            warnings.Add($"Attachment {attachmentId.Value} no longer exists; nothing to remove.");
            return warnings;
        }

        // Physical delete (D5) — the only table where Undo is destructive by design.
        _db.TaskAttachments.Remove(attachment);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "PostAttachment undone (log {LogId}, attachment {AttachmentId}) — row physically deleted.",
            log.Id, attachmentId.Value);

        return warnings;
    }

    private static AgentAttachmentSnapshot Parse(AiActionLog log)
    {
        if (string.IsNullOrWhiteSpace(log.AfterSnapshot))
        {
            throw new BadRequestException("The AI action has no after_snapshot to apply.");
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<AgentAttachmentSnapshot>(log.AfterSnapshot, Json)
                           ?? throw new BadRequestException("The AI action after_snapshot is empty.");

            if (snapshot.TaskId == Guid.Empty || string.IsNullOrWhiteSpace(snapshot.ContentBase64))
            {
                throw new BadRequestException("The AI action after_snapshot is missing taskId or contentBase64.");
            }

            return snapshot;
        }
        catch (JsonException ex)
        {
            throw new BadRequestException($"The AI action after_snapshot is not valid JSON: {ex.Message}");
        }
    }
}
