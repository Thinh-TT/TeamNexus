using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Knowledge;

/// <summary>
/// The pgvector-backed implementation of the Board module's <see cref="IWorkspaceKnowledgeStore"/> port
/// (Giai đoạn 14.7 Track B §B3). Registered by <c>AddAiModule</c>, which runs after <c>AddBoardModule</c> and
/// therefore wins over <see cref="NullWorkspaceKnowledgeStore"/>.
/// <para>
/// <b>The query is the feature.</b> One <c>ORDER BY embedding &lt;=&gt; @query LIMIT n</c> is the whole point of
/// having chosen pgvector: the ranking happens inside the database against the HNSW index, so the cost is a
/// function of <c>n</c> rather than of how many chunks the workspace has accumulated.
/// </para>
/// <para>
/// <b>Scoping is by workspace, from the parameters.</b> The workspace id comes from the caller (which got it
/// from the board), never from a model or a request body — the same rule every agent tool follows.
/// </para>
/// <para>
/// <b>Every failure is an empty result.</b> A missing extension, a dimension mismatch, a database hiccup: the
/// caller is composing a chat prompt, and a prompt without the knowledge block is degraded, not broken. Throwing
/// here would turn "the knowledge base is unwell" into "the chat is dead", which is the wrong trade every time.
/// </para>
/// </summary>
public sealed class WorkspaceKnowledgeStore : IWorkspaceKnowledgeStore
{
    /// <summary>
    /// Upper bound on rows the query may return before the caller's own caps are applied. A caller asking for
    /// 1000 chunks is asking for a prompt it cannot afford, so the ceiling lives here too.
    /// </summary>
    private const int MaxChunks = 50;

    private readonly TeamNexusDbContext _db;
    private readonly ILogger<WorkspaceKnowledgeStore> _logger;

    public WorkspaceKnowledgeStore(TeamNexusDbContext db, ILogger<WorkspaceKnowledgeStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<KnowledgeChunk>> GetTopChunksAsync(
        Guid workspaceId,
        ReadOnlyMemory<float> queryVector,
        bool onlyReady,
        int maxDocuments,
        int maxChunks,
        CancellationToken ct = default)
    {
        if (workspaceId == Guid.Empty || queryVector.Length == 0 || maxChunks <= 0)
        {
            // Nothing to search with or for. Returned rather than thrown because "no query" is a legitimate
            // state (RAG off, no documents) and the caller must not have to special-case it.
            return [];
        }

        var take = Math.Clamp(maxChunks, 1, MaxChunks);
        var documentCap = Math.Max(1, maxDocuments);

        try
        {
            // The denormalised `workspace_id` on the chunk is what makes this a single-table query: no join to
            // the document for the scope, only for the title below.
            var query = _db.WorkspaceKnowledgeChunks
                .AsNoTracking()
                .Where(c => c.WorkspaceId == workspaceId && c.Embedding != null);

            if (onlyReady)
            {
                // `status = ready` AND the document still visible (its own query filter applies through the
                // navigation). A document that is still `pending` has chunks without embeddings, so this also
                // avoids ranking NULL vectors.
                query = query.Where(c => c.Document != null
                                         && c.Document.Status == KnowledgeDocumentStatus.Ready);
            }

            var vector = new Vector(queryVector.ToArray());

            // One row per document: a workspace with a single long document would otherwise fill the whole
            // result set from one source, and the model would lose all variety. Grouping after the distance
            // sort means the nearest chunk per document wins, which is the intent.
            var nearest = await query
                .Where(c => c.Document != null
                            && c.Document.Status != KnowledgeDocumentStatus.Deleted)
                .Select(c => new
                {
                    c.DocumentId,
                    c.ChunkIndex,
                    c.Content,
                    Title = c.Document!.Title,
                    Distance = c.Embedding!.CosineDistance(vector),
                })
                // Nearest first. No threshold is applied: cosine distance is unbounded above, so any fixed cut
                // would be arbitrary. `take` is the real budget control.
                .OrderBy(c => c.Distance)
                // A generous pre-limit (one chunk per document is what survives the grouping below, so reading
                // `take * documentCap` rows is enough to find the best chunk of each candidate document).
                .Take(take * documentCap)
                .ToListAsync(ct);

            var chunks = nearest
                .GroupBy(row => row.DocumentId)
                .Select(group => group.OrderBy(row => row.Distance).First())
                .OrderBy(row => row.Distance)
                .Take(Math.Min(take, documentCap))
                .Select(row => new KnowledgeChunk(row.Title, row.Content))
                .ToList();

            if (chunks.Count > 0)
            {
                _logger.LogDebug(
                    "Knowledge base: {Count} đoạn kiến thức gần nhất cho workspace {WorkspaceId}.",
                    chunks.Count,
                    workspaceId);
            }

            return chunks;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Covers the "extension vector is missing" case (0A000/58P01) as well as any dimension mismatch:
            // the chat must still answer, just without the knowledge block.
            _logger.LogWarning(
                ex,
                "Không truy vấn được bộ nhớ ngữ nghĩa cho workspace {WorkspaceId}; bỏ qua khối kiến thức.",
                workspaceId);

            return [];
        }
    }
}
