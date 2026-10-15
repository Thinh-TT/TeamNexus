using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Ai.Services.Jev;
using TeamNexus.Modules.Ai.Services.Knowledge;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Appliers;

/// <summary>
/// Applier for <see cref="AiActionTypes.CreateKnowledgeDocument"/> (Giai đoạn 14.7 Track B §B3): writes an
/// approved knowledge document and its chunks, and soft-deletes them on Undo.
/// <para>
/// <b>This is a real data-writing action and it goes through the Accountability Layer like every other one.</b>
/// It would have been tempting to write the row directly at request time — "it is only a document" — and that
/// is exactly how a gateway stops being a gateway. Approving writes; rejecting writes nothing; Undo restores
/// the previous state.
/// </para>
/// <para>
/// <b>⛔ No transaction is opened here.</b> <see cref="AiActionService.ApproveAsync"/> already opened one, and
/// nesting a second is the Phase 14.5 <b>P2</b> bug (a nested transaction inside an ambient one made approving a
/// move answer 500). The document and all of its chunks therefore land in the caller's single
/// <c>SaveChangesAsync</c> — which is also what makes "document exists with zero chunks" impossible.
/// </para>
/// <para>
/// <b>Embeddings are NOT produced here.</b> Approving makes the document and its text retrievable-in-principle;
/// the embedding pass is a separate, retryable background step, so an embedder outage can never make the
/// approve button fail. The document is created <c>pending</c> and the runner promotes it to <c>ready</c>.
/// </para>
/// </summary>
public sealed class CreateKnowledgeDocumentApplier : IAiActionApplier
{
    /// <summary>Matches <c>workspace_knowledge_documents.title</c>.</summary>
    public const int MaxTitleLength = 300;

    /// <summary>Matches <c>workspace_knowledge_documents.source_url</c>.</summary>
    public const int MaxSourceUrlLength = 1000;

    /// <summary>Matches <c>workspace_knowledge_documents.error</c>.</summary>
    public const int MaxErrorLength = 500;

    /// <summary>Upper bound on the submitted text, so one approve cannot index an unbounded document.</summary>
    public const int MaxContentChars = 400_000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TeamNexusDbContext _db;
    private readonly JevOptions _jev;
    private readonly ILogger<CreateKnowledgeDocumentApplier> _logger;

    public CreateKnowledgeDocumentApplier(
        TeamNexusDbContext db,
        IOptions<JevOptions> jev,
        ILogger<CreateKnowledgeDocumentApplier> logger)
    {
        _db = db;
        _jev = jev.Value;
        _logger = logger;
    }

    public string ActionType => AiActionTypes.CreateKnowledgeDocument;

    /// <summary>
    /// <c>after_snapshot.kind</c> value meaning "remove this document" (see the class remarks: one action type,
    /// operation in the payload).
    /// </summary>
    public const string DeleteKind = "Delete";

    public async Task<AiActionAppliedResult> ApplyAsync(
        AiActionLog log, AiActionContext ctx, CancellationToken ct = default)
    {
        var payload = ParsePayload(log);

        if (string.Equals(payload.Kind?.Trim(), DeleteKind, StringComparison.OrdinalIgnoreCase))
        {
            return await DeleteAsync(log, ctx, payload, ct);
        }

        var title = Cap(payload.Title?.Trim(), MaxTitleLength);

        if (string.IsNullOrEmpty(title))
        {
            throw new BadRequestException("Tài liệu kiến thức phải có tiêu đề.");
        }

        var content = payload.Content ?? string.Empty;

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new BadRequestException("Tài liệu kiến thức phải có nội dung.");
        }

        if (content.Length > MaxContentChars)
        {
            // Refused rather than truncated: silently indexing a prefix would make the document's answers
            // subtly incomplete, which is far harder to notice than a clear rejection.
            throw new BadRequestException(
                $"Nội dung tài liệu tối đa {MaxContentChars} ký tự (nhận {content.Length}).");
        }

        var effective = _jev.Effective;
        var plan = KnowledgeChunking.Plan(content, effective.RagChunkChars, effective.RagMaxChunksPerDocument);

        if (plan.Chunks.Count == 0)
        {
            throw new BadRequestException("Không tách được đoạn nội dung nào từ tài liệu.");
        }

        var document = new WorkspaceKnowledgeDocument
        {
            Id = Guid.NewGuid(),
            WorkspaceId = ctx.WorkspaceId,
            Title = title,
            SourceUrl = Cap(payload.SourceUrl?.Trim(), MaxSourceUrlLength),
            Content = content,
            // Pending, never Ready: "ready" means "every chunk has an embedding", and the embeddings do not
            // exist yet. Claiming ready here would make retrieval silently return nothing for a document the UI
            // shows as indexed.
            Status = KnowledgeDocumentStatus.Pending,
            ChunkCount = plan.Chunks.Count,
            CreatedByUserId = log.RequestedByUserId,
        };

        _db.WorkspaceKnowledgeDocuments.Add(document);

        foreach (var chunk in plan.Chunks)
        {
            _db.WorkspaceKnowledgeChunks.Add(new WorkspaceKnowledgeChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                // Denormalised from the document (single-table retrieval); the two can never disagree because
                // they are written together, in one transaction.
                WorkspaceId = ctx.WorkspaceId,
                ChunkIndex = chunk.Index,
                Content = chunk.Content,
                Embedding = null,
            });
        }

        var warnings = new List<string>();

        if (plan.Truncated)
        {
            // A visible warning, because silently indexing the first N chunks of a long document would let the
            // knowledge base answer as if the rest did not exist.
            warnings.Add(
                $"Tài liệu dài hơn giới hạn: chỉ {plan.Chunks.Count} đoạn đầu được đánh chỉ mục "
                + $"({plan.DroppedChunks} đoạn bị bỏ qua).");
        }

        if (!_jev.RagEnabled)
        {
            warnings.Add("Bộ nhớ ngữ nghĩa đang tắt (Jev:RagEnabled=false): tài liệu chưa tham gia tìm kiếm.");
        }

        // ⛔ The save is OURS to make. `AiActionService.ApproveAsync` opens the transaction and commits it, but
        // it never calls `SaveChangesAsync` — every other applier reaches a domain service that saves for it.
        // This one writes its rows directly, so without this line the document and its chunks are tracked, the
        // action is reported `Approved` with a `createdDocumentId`, and the transaction commits having written
        // nothing at all. One save for both tables is also what makes "document exists with zero chunks"
        // impossible.
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Knowledge document {DocumentId} applied for workspace {WorkspaceId}: {Chunks} chunk(s), "
            + "{Dropped} dropped, truncated={Truncated}.",
            document.Id,
            ctx.WorkspaceId,
            plan.Chunks.Count,
            plan.DroppedChunks,
            plan.Truncated);

        return new AiActionAppliedResult(
            AiEntityTypes.Workspace,
            ctx.WorkspaceId,
            [],
            [],
            warnings,
            CreatedDocumentId: document.Id,
            CreatedChunkCount: plan.Chunks.Count);
    }

    /// <summary>
    /// The removal branch of the same action type: soft-delete one named document of the same workspace.
    /// <para>
    /// <b>The document id is re-derived from the workload, not trusted from the payload alone.</b> The query
    /// filters on <c>WorkspaceId</c>, so a payload naming a document of another workspace cannot delete it —
    /// the same "scope comes from the context" rule the agent tools follow.
    /// </para>
    /// </summary>
    private async Task<AiActionAppliedResult> DeleteAsync(
        AiActionLog log, AiActionContext ctx, KnowledgeDocumentPayload payload, CancellationToken ct)
    {
        if (payload.DocumentId is not { } documentId || documentId == Guid.Empty)
        {
            throw new BadRequestException("Đề xuất xoá tài liệu thiếu id tài liệu.");
        }

        var document = await _db.WorkspaceKnowledgeDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId && d.WorkspaceId == ctx.WorkspaceId, ct)
            ?? throw new NotFoundException("Không tìm thấy tài liệu cần xoá trong workspace này.");

        var warnings = new List<string>();

        if (document.Status == KnowledgeDocumentStatus.Deleted)
        {
            // Already gone (a duplicate approval, or a soft-delete from another route). Reported rather than
            // treated as an error: the desired state is already true.
            warnings.Add("Tài liệu đã ở trạng thái đã xoá.");
        }
        else
        {
            document.Status = KnowledgeDocumentStatus.Deleted;
        }

        // Same reason as the create branch: the status change is a tracked-entity edit and nothing else in the
        // approve path saves it, so without this the "deleted" document stays readable.
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Knowledge document {DocumentId} removed (approved action {LogId}, workspace {WorkspaceId}).",
            document.Id,
            log.Id,
            ctx.WorkspaceId);

        return new AiActionAppliedResult(
            AiEntityTypes.Workspace,
            ctx.WorkspaceId,
            [],
            [],
            warnings,
            CreatedDocumentId: document.Id);
    }

    /// <summary>
    /// Soft-deletes the document (and leaves its chunks in place).
    /// <para>
    /// <b>Soft, not hard, and symmetric for both branches.</b> The chunks carry embeddings that cost money and a
    /// re-approve is a normal thing to want; and <c>status = 'deleted'</c> reuses a column that already exists,
    /// so Undo needs no third migration and no <c>deleted_at</c> timestamp. Retrieval excludes non-ready
    /// statuses, so a deleted document stops being visible immediately.
    /// </para>
    /// <para>
    /// Undoing a <b>deletion</b> therefore restores the document to <c>Ready</c> when it still has vectors, and
    /// to <c>Pending</c> otherwise — the state it must have to become retrievable again without claiming an
    /// embedding it does not have.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<string>> UndoAsync(
        AiActionLog log, AiActionContext ctx, CancellationToken ct = default)
    {
        var documentId = AiActionService.ReadGuid(log.AppliedSnapshot, "createdDocumentId");

        if (documentId is null)
        {
            // An approved action that cannot be undone would sit in the history forever, so this returns a
            // warning rather than throwing: the caller has already committed the approval.
            return ["Không xác định được tài liệu cần hoàn tác."];
        }

        var document = await _db.WorkspaceKnowledgeDocuments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == documentId.Value, ct);

        if (document is null)
        {
            return ["Tài liệu đã bị xoá khỏi hệ thống; không còn gì để hoàn tác."];
        }

        if (document.Status != KnowledgeDocumentStatus.Deleted)
        {
            // Nothing to restore: the action was a creation (no delete happened) or the document was already
            // restored. Returning success keeps a retried undo from looking like a failure.
            return [];
        }

        var hasChunks = await _db.WorkspaceKnowledgeChunks
            .AnyAsync(c => c.DocumentId == document.Id, ct);

        var hasPendingChunks = await _db.WorkspaceKnowledgeChunks
            .AnyAsync(c => c.DocumentId == document.Id && c.Embedding == null, ct);

        // The state it must have to become retrievable again WITHOUT claiming an embedding it does not have:
        // `ready` only when every chunk it owns already carries a vector.
        document.Status = hasChunks && !hasPendingChunks
            ? KnowledgeDocumentStatus.Ready
            : KnowledgeDocumentStatus.Pending;

        // Undo runs in the service's transaction too, and nothing else in that path saves; without this the
        // restore is computed, logged, and silently discarded.
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Knowledge document {DocumentId} restored to {Status} by Undo of action {LogId}.",
            document.Id,
            document.Status,
            log.Id);

        return [];
    }

    /// <summary>
    /// Parses the reviewed payload. Every field is optional here and validated after, so one malformed value
    /// produces one clear message rather than a deserialization stack trace.
    /// <para>
    /// <c>after_snapshot</c> is untrusted input — it round-tripped through the client's preview — so nothing in it
    /// is believed about identity or scope: the workspace comes from <see cref="AiActionContext"/>, never from the
    /// payload.
    /// </para>
    /// </summary>
    private static KnowledgeDocumentPayload ParsePayload(AiActionLog log)
    {
        if (string.IsNullOrWhiteSpace(log.AfterSnapshot))
        {
            throw new BadRequestException("Đề xuất tài liệu không có nội dung.");
        }

        try
        {
            return JsonSerializer.Deserialize<KnowledgeDocumentPayload>(log.AfterSnapshot, Json)
                   ?? throw new BadRequestException("Đề xuất tài liệu không đọc được.");
        }
        catch (JsonException)
        {
            throw new BadRequestException("Đề xuất tài liệu không phải JSON hợp lệ.");
        }
    }

    /// <summary>Truncates to the column limit; null/blank stays null so the column keeps its "absent" meaning.</summary>
    private static string? Cap(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    /// <summary>
    /// The reviewed payload. camelCase to match the wire shape the rest of the action layer stores.
    /// <para>
    /// <c>Kind</c> is absent for a creation and <c>"Delete"</c> for a removal — one action type, operation in
    /// the payload (the <c>RecoveryPlan</c> precedent).
    /// </para>
    /// </summary>
    private sealed record KnowledgeDocumentPayload(
        string? Kind,
        string? Title,
        string? SourceUrl,
        string? Content,
        Guid? DocumentId);
}
