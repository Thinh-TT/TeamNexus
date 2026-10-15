using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Knowledge;

/// <summary>
/// One embedding pass over the chunks that do not have a vector yet (Giai đoạn 14.7 Track B §B3).
/// </summary>
/// <param name="Skipped">True when there was nothing to do (feature off, or no pending chunks).</param>
/// <param name="Embedded">Chunks that received an embedding this pass.</param>
/// <param name="DocumentsPromoted">Documents that moved to <c>ready</c> this pass.</param>
/// <param name="DocumentsFailed">Documents that moved to <c>failed</c> this pass.</param>
public sealed record KnowledgeEmbeddingRunResult(
    bool Skipped,
    int Embedded,
    int DocumentsPromoted,
    int DocumentsFailed)
{
    public static KnowledgeEmbeddingRunResult NotRun { get; } = new(true, 0, 0, 0);
}

/// <summary>
/// The pass itself, extracted from the hosted service so a test can call it <b>directly</b> — no timer, no
/// startup delay, no waiting (the <c>DailyDigestRunner</c>/<c>AgentRunReaperRunner</c> precedent).
/// </summary>
public interface IKnowledgeEmbeddingRunner
{
    Task<KnowledgeEmbeddingRunResult> RunOnceAsync(CancellationToken ct = default);
}

/// <summary>
/// Produces the embeddings that make knowledge retrievable, and moves each document to a truthful status.
/// <para>
/// <b>Why this is not part of the approve path.</b> Approving is a human action that must succeed; embedding is
/// a network call to a third party. Coupling them would mean an embedder outage makes the approve button fail —
/// which would be the worst possible place for that failure, because the human has no other way to add
/// knowledge. So the document is written <c>pending</c>, and this pass promotes it later, retrying on the next
/// tick for as long as <c>embedding IS NULL</c>.
/// </para>
/// <para>
/// <b>The status is derived, never assumed.</b> A document becomes <c>ready</c> only when it has at least one
/// chunk and <b>no</b> chunk is missing a vector, because "ready" is the flag that admits it into retrieval —
/// setting it early would let the knowledge base answer from a half-indexed document with no indication that it
/// had done so. A document whose embedding raised becomes <c>failed</c> with the reason stored, so an operator
/// can see what happened instead of watching a document sit silently in <c>pending</c>.
/// </para>
/// <para>
/// <b>Bounded per pass.</b> Chunks are taken in batches so a first pass over a large backlog cannot hold a
/// connection and a scope for minutes; whatever is left waits for the next tick, which is harmless because the
/// work is idempotent.
/// </para>
/// </summary>
public sealed class KnowledgeEmbeddingRunner : IKnowledgeEmbeddingRunner
{
    /// <summary>Chunks per embedding request. Small enough to stay inside a provider's input limit.</summary>
    public const int BatchSize = 32;

    /// <summary>Upper bound on chunks per pass, so one tick cannot run unbounded.</summary>
    public const int MaxChunksPerPass = 512;

    /// <summary>Matches <c>workspace_knowledge_documents.error</c>.</summary>
    private const int MaxErrorLength = 500;

    private readonly TeamNexusDbContext _db;
    private readonly IEmbeddingProvider _provider;
    private readonly EmbeddingOptions _options;
    private readonly ILogger<KnowledgeEmbeddingRunner> _logger;

    public KnowledgeEmbeddingRunner(
        TeamNexusDbContext db,
        IEmbeddingProvider provider,
        IOptions<EmbeddingOptions> options,
        ILogger<KnowledgeEmbeddingRunner> logger)
    {
        _db = db;
        _provider = provider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<KnowledgeEmbeddingRunResult> RunOnceAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Embeddings đang tắt (Embeddings:Enabled=false) — bỏ qua lượt sinh vector.");
            return KnowledgeEmbeddingRunResult.NotRun;
        }

        // Only chunks of live documents: a soft-deleted document's chunks keep their vectors (so a re-approve is
        // cheap) but must not consume embedding calls.
        var pending = await _db.WorkspaceKnowledgeChunks
            .Where(c => c.Embedding == null
                        && c.Document != null
                        && c.Document.Status == KnowledgeDocumentStatus.Pending)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Take(MaxChunksPerPass)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            return KnowledgeEmbeddingRunResult.NotRun;
        }

        var embedded = 0;
        var failedDocuments = new HashSet<Guid>();
        var touchedDocuments = pending.Select(c => c.DocumentId).Distinct().ToList();

        foreach (var batch in pending.Chunk(BatchSize))
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var texts = batch.Select(c => c.Content).ToList();

            IReadOnlyList<ReadOnlyMemory<float>> vectors;

            try
            {
                vectors = await _provider.EmbedAsync(texts, ct);
            }
            catch (EmbeddingProviderException ex)
            {
                // The whole batch fails together: record the reason on its documents and stop this pass. Retrying
                // the same batch against an outage would just burn the run's wall-clock budget.
                _logger.LogWarning(ex, "Sinh vector thất bại cho {Count} đoạn; sẽ thử lại ở lượt sau.", batch.Length);

                foreach (var documentId in batch.Select(c => c.DocumentId).Distinct())
                {
                    failedDocuments.Add(documentId);
                    await MarkDocumentAsync(documentId, KnowledgeDocumentStatus.Failed, Cap(ex.Message), ct);
                }

                break;
            }

            if (vectors.Count != batch.Length)
            {
                _logger.LogWarning(
                    "Nhà cung cấp embedding trả về {Returned} vector cho {Expected} đoạn; bỏ qua lô này.",
                    vectors.Count,
                    batch.Length);

                foreach (var documentId in batch.Select(c => c.DocumentId).Distinct())
                {
                    failedDocuments.Add(documentId);
                    await MarkDocumentAsync(
                        documentId,
                        KnowledgeDocumentStatus.Failed,
                        $"Số vector trả về ({vectors.Count}) không khớp số đoạn ({batch.Length}).",
                        ct);
                }

                break;
            }

            for (var index = 0; index < batch.Length; index++)
            {
                var vector = vectors[index];

                if (vector.Length != _options.Dimensions)
                {
                    // A wrong-length vector must never reach the database: the column is vector(1536) and the
                    // insert would fail with a message that says nothing about which document caused it.
                    _logger.LogWarning(
                        "Vector có {Length} chiều, cấu hình yêu cầu {Expected}; đánh dấu tài liệu thất bại.",
                        vector.Length,
                        _options.Dimensions);

                    var documentId = batch[index].DocumentId;
                    failedDocuments.Add(documentId);
                    await MarkDocumentAsync(
                        documentId,
                        KnowledgeDocumentStatus.Failed,
                        $"Vector có {vector.Length} chiều, schema yêu cầu {_options.Dimensions}.",
                        ct);

                    continue;
                }

                batch[index].Embedding = new Pgvector.Vector(vector.ToArray());
                embedded++;
            }

            await _db.SaveChangesAsync(ct);
        }

        var promoted = 0;

        foreach (var documentId in touchedDocuments.Where(id => !failedDocuments.Contains(id)))
        {
            if (await TryPromoteAsync(documentId, ct))
            {
                promoted++;
            }
        }

        var result = new KnowledgeEmbeddingRunResult(false, embedded, promoted, failedDocuments.Count);

        _logger.LogInformation(
            "Embedding pass: {Embedded} đoạn, {Promoted} tài liệu ready, {Failed} tài liệu failed.",
            result.Embedded,
            result.DocumentsPromoted,
            result.DocumentsFailed);

        return result;
    }

    /// <summary>
    /// Flips a document to <c>ready</c> only when it genuinely has no pending chunk left. Returns false when it
    /// must stay <c>pending</c> (more chunks waiting, or a chunk was added after this pass started).
    /// </summary>
    private async Task<bool> TryPromoteAsync(Guid documentId, CancellationToken ct)
    {
        var document = await _db.WorkspaceKnowledgeDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (document is null || document.Status != KnowledgeDocumentStatus.Pending)
        {
            return false;
        }

        var remaining = await _db.WorkspaceKnowledgeChunks
            .CountAsync(c => c.DocumentId == documentId && c.Embedding == null, ct);

        var total = await _db.WorkspaceKnowledgeChunks
            .CountAsync(c => c.DocumentId == documentId, ct);

        if (remaining > 0 || total == 0)
        {
            return false;
        }

        document.Status = KnowledgeDocumentStatus.Ready;
        document.Error = null;
        await _db.SaveChangesAsync(ct);

        return true;
    }

    /// <summary>Records a failure on the document without letting a second failure overwrite the first reason.</summary>
    private async Task MarkDocumentAsync(
        Guid documentId, KnowledgeDocumentStatus status, string? error, CancellationToken ct)
    {
        var document = await _db.WorkspaceKnowledgeDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (document is null)
        {
            return;
        }

        document.Status = status;

        if (!string.IsNullOrWhiteSpace(error))
        {
            document.Error = error;
        }

        await _db.SaveChangesAsync(ct);
    }

    private static string? Cap(string? value)
        => string.IsNullOrEmpty(value) || value.Length <= MaxErrorLength
            ? value
            : value[..MaxErrorLength];
}

/// <summary>
/// Periodic driver for <see cref="IKnowledgeEmbeddingRunner"/>: asks for a pass on an interval.
/// <para>
/// Deliberately not the only caller — an integration test invokes the runner directly. The tick is dumb: the
/// runner decides what needs work, and the work is idempotent, so a tick that fires too early or twice is
/// harmless. A missing API key is not a reason to skip the tick either: the offline provider still produces
/// deterministic vectors, which is what makes the whole feature verifiable without network access.
/// </para>
/// <para>
/// Defensive like every other background service: an escaping exception would stop the host, and knowledge
/// indexing is never worth taking the API down.
/// </para>
/// </summary>
public sealed class KnowledgeEmbeddingBackgroundService : BackgroundService
{
    /// <summary>How often a pass is attempted. Short, because a pass with nothing to do is one cheap query.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    /// <summary>Delay before the first pass, so it never competes with startup migration/seed work.</summary>
    public const int StartupDelaySeconds = 120;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmbeddingOptions _options;
    private readonly ILogger<KnowledgeEmbeddingBackgroundService> _logger;

    public KnowledgeEmbeddingBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<EmbeddingOptions> options,
        ILogger<KnowledgeEmbeddingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Knowledge embedding service started (enabled={Enabled}, model={Model}, dimensions={Dimensions}, "
            + "interval={Interval}).",
            _options.Enabled,
            _options.EffectiveModel,
            _options.Dimensions,
            Interval);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(StartupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    return;
                }

                await using var scope = _scopeFactory.CreateAsyncScope();
                var runner = scope.ServiceProvider.GetRequiredService<IKnowledgeEmbeddingRunner>();

                await runner.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lượt sinh vector thất bại; timer vẫn chạy tiếp.");
            }
        }
    }
}
