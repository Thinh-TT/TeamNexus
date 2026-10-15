using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Ai.Services.Jev;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Knowledge;

/// <summary>
/// The workspace knowledge base (Giai đoạn 14.7 Track B §B3): list, request, delete — and the retrievability
/// rules that decide what may be used as context.
/// <para>
/// <b>Every write goes through the Accountability Layer.</b> Adding a document means embedding it, which costs
/// money and changes what the AI can see, so it is a <c>Pending</c> <c>CreateKnowledgeDocument</c> action that a
/// Manager approves. Nothing in this service inserts a document row.
/// </para>
/// <para>
/// <b>Reads are honest about the switch.</b> With <c>Jev:RagEnabled = false</c> the list returns an empty set
/// plus <c>enabled = false</c> — never a "success" that implies the AI is using knowledge it cannot see.
/// </para>
/// </summary>
public interface IKnowledgeBaseService
{
    /// <summary>Member+; unknown workspace ⇒ 404.</summary>
    Task<KnowledgeDocumentListResponse> ListAsync(
        Guid workspaceId, Guid userId, CancellationToken ct = default);

    /// <summary>Manager+; records a <c>Pending</c> <c>CreateKnowledgeDocument</c> action and writes nothing.</summary>
    Task<AiActionLogResponse> CreateAsync(
        Guid workspaceId, CreateKnowledgeDocumentRequest request, Guid userId, CancellationToken ct = default);

    /// <summary>Manager+; records a <c>Pending</c> deletion action for one document.</summary>
    Task<AiActionLogResponse> DeleteAsync(
        Guid workspaceId, Guid documentId, Guid userId, CancellationToken ct = default);
}

/// <inheritdoc cref="IKnowledgeBaseService" />
public sealed class KnowledgeBaseService : IKnowledgeBaseService
{
    /// <summary>Human name of the IHttpClientFactory client used for embedding calls.</summary>
    public const string HttpClientName = "Embeddings";

    /// <summary>Matches <c>workspace_knowledge_documents.title</c>.</summary>
    public const int MaxTitleLength = 300;

    /// <summary>Matches <c>workspace_knowledge_documents.source_url</c>.</summary>
    public const int MaxSourceUrlLength = 1_000;

    /// <summary>Upper bound on submitted text, mirroring the applier's own ceiling.</summary>
    public const int MaxContentChars = 400_000;

    /// <summary>Upper bound on one list page, so one request cannot pull a whole knowledge base.</summary>
    public const int MaxListTake = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IAiActionService _actions;
    private readonly JevOptions _jev;
    private readonly ILogger<KnowledgeBaseService> _logger;

    public KnowledgeBaseService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IAiActionService actions,
        IOptions<JevOptions> jev,
        ILogger<KnowledgeBaseService> logger)
    {
        _db = db;
        _access = access;
        _actions = actions;
        _jev = jev.Value;
        _logger = logger;
    }

    public async Task<KnowledgeDocumentListResponse> ListAsync(
        Guid workspaceId, Guid userId, CancellationToken ct = default)
    {
        // Membership first: a non-member gets 404 from the access service, so this endpoint never confirms that
        // a workspace exists.
        await _access.RequireMemberAsync(workspaceId, userId, ct);

        // RAG off ⇒ an empty list WITH the flag. Returning rows here would tell the caller the AI can use
        // knowledge that retrieval will in fact skip.
        if (!_jev.RagEnabled)
        {
            return new KnowledgeDocumentListResponse(false, []);
        }

        var documents = await _db.WorkspaceKnowledgeDocuments
            .AsNoTracking()
            .Where(d => d.WorkspaceId == workspaceId && d.Status != KnowledgeDocumentStatus.Deleted)
            .OrderByDescending(d => d.CreatedAt)
            .Take(MaxListTake)
            .Select(d => new KnowledgeDocumentResponse(
                d.Id,
                d.Title,
                d.SourceUrl,
                d.Status.ToString().ToLowerInvariant(),
                d.ChunkCount,
                d.Error,
                d.CreatedAt,
                d.UpdatedAt))
            .ToListAsync(ct);

        return new KnowledgeDocumentListResponse(true, documents);
    }

    public async Task<AiActionLogResponse> CreateAsync(
        Guid workspaceId, CreateKnowledgeDocumentRequest request, Guid userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Validated BEFORE the accountability row is written: a 400 must not leave a Pending action behind for a
        // document that could never be applied.
        var title = request.Title?.Trim();

        if (string.IsNullOrEmpty(title) || title.Length > MaxTitleLength)
        {
            throw new BadRequestException($"Tiêu đề tài liệu phải có 1–{MaxTitleLength} ký tự.");
        }

        var content = request.Content ?? string.Empty;

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new BadRequestException("Nội dung tài liệu không được để trống.");
        }

        if (content.Length > MaxContentChars)
        {
            throw new BadRequestException(
                $"Nội dung tài liệu tối đa {MaxContentChars} ký tự (nhận {content.Length}).");
        }

        var sourceUrl = request.SourceUrl?.Trim();

        if (sourceUrl is { Length: > MaxSourceUrlLength })
        {
            throw new BadRequestException($"Đường dẫn nguồn tối đa {MaxSourceUrlLength} ký tự.");
        }

        if (!_jev.RagEnabled)
        {
            // Refused rather than accepted-and-ignored: a document added while RAG is off would sit in the list
            // looking indexed while nothing could retrieve it, which is worse than a clear refusal.
            throw new BadRequestException(
                "Bộ nhớ ngữ nghĩa đang tắt (Jev:RagEnabled=false) nên chưa thể thêm tài liệu.");
        }

        var effective = _jev.Effective;
        var plan = KnowledgeChunking.Plan(content, effective.RagChunkChars, effective.RagMaxChunksPerDocument);

        // The after_snapshot is exactly what the applier will read back, built by the same shaping rules
        // (title/url capped, content whole) so approving applies what the human reviewed.
        var afterSnapshot = JsonSerializer.Serialize(new
        {
            title,
            sourceUrl,
            content,
        }, Json);

        var basis = JsonSerializer.Serialize(new
        {
            source = "KnowledgeBase",
            workspaceId,
            chunkCount = plan.Chunks.Count,
            droppedChunks = plan.DroppedChunks,
            contentLength = content.Length,
            requestedAt = DateTimeOffset.UtcNow,
        }, Json);

        var log = await _actions.RequestCreateKnowledgeDocumentAsync(
            workspaceId, userId, afterSnapshot, basis, ct);

        _logger.LogInformation(
            "Knowledge document '{Title}' requested for workspace {WorkspaceId} as Pending action {LogId} "
            + "({Chunks} chunk(s) planned).",
            title,
            workspaceId,
            log.Id,
            plan.Chunks.Count);

        return log;
    }

    public async Task<AiActionLogResponse> DeleteAsync(
        Guid workspaceId, Guid documentId, Guid userId, CancellationToken ct = default)
    {
        var document = await _db.WorkspaceKnowledgeDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.WorkspaceId == workspaceId, ct)
            ?? throw new NotFoundException("Không tìm thấy tài liệu trong workspace này.");

        if (document.Status == KnowledgeDocumentStatus.Deleted)
        {
            // Idempotent: deleting something already deleted is a retry, not an error.
            throw new BadRequestException("Tài liệu này đã được xoá trước đó.");
        }

        // One action type, operation in the payload (the RecoveryPlan precedent) — the applier switches on
        // `kind`, so the AI history keeps a single entry per feature instead of two fragmented streams.
        var afterSnapshot = JsonSerializer.Serialize(new
        {
            kind = "Delete",
            documentId = document.Id,
            title = document.Title,
        }, Json);

        var basis = JsonSerializer.Serialize(new
        {
            source = "KnowledgeBase",
            workspaceId,
            documentId = document.Id,
            requestedAt = DateTimeOffset.UtcNow,
        }, Json);

        var log = await _actions.RequestCreateKnowledgeDocumentAsync(
            workspaceId, userId, afterSnapshot, basis, ct);

        _logger.LogInformation(
            "Knowledge document {DocumentId} requested for deletion in workspace {WorkspaceId} as Pending "
            + "action {LogId}.",
            documentId,
            workspaceId,
            log.Id);

        return log;
    }
}
