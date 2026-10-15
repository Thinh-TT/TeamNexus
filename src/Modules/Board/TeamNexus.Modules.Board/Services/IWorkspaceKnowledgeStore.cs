namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// One retrievable slice of workspace knowledge, as the Board side needs to describe it
/// (Giai đoạn 14.7 Track B §B3).
/// <para>
/// Carries the document <b>title</b> and the text, never an id: the consumer puts this straight into a model
/// prompt, and a prompt that contains no identifiers cannot leak one.
/// </para>
/// </summary>
public sealed record KnowledgeChunk(string DocumentTitle, string Content);

/// <summary>
/// The read half of the semantic knowledge base, declared in <b>Board</b> (Giai đoạn 14.7 Track B §B3).
/// <para>
/// <b>Why the port lives here.</b> <c>AgentChatService</c> builds the chat prompt and lives in the Ai module,
/// so on the face of it a Board port is unnecessary. It is here for the mirror-image reason of every other port
/// in this file: Board must never reference Ai, and the knowledge block is <i>board</i> context. Keeping the
/// contract on the Board side means the Ai module supplies the implementation (registered by
/// <c>AddAiModule</c>, which wins the resolve) exactly like <c>IDailyDigestService</c>,
/// <c>IActivityLogWriter</c> and <c>INotificationWriter</c> — one recurring pattern instead of a new one.
/// </para>
/// <para>
/// <b>It never throws for "no knowledge".</b> An empty result — RAG off, nothing embedded yet, a workspace with
/// no documents — is a normal answer, so the chat path can call it unconditionally and simply leave the block
/// out.
/// </para>
/// </summary>
public interface IWorkspaceKnowledgeStore
{
    /// <summary>
    /// The <paramref name="maxChunks"/> nearest chunks to <paramref name="queryVector"/> across at most
    /// <paramref name="maxDocuments"/> documents of the workspace, nearest first.
    /// <para>
    /// <paramref name="onlyReady"/> restricts the search to documents whose embeddings are complete. It is a
    /// parameter rather than a hard-coded rule because the embedding runner needs the wider view; every prompt
    /// path passes <c>true</c>.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<KnowledgeChunk>> GetTopChunksAsync(
        Guid workspaceId,
        ReadOnlyMemory<float> queryVector,
        bool onlyReady,
        int maxDocuments,
        int maxChunks,
        CancellationToken ct = default);
}

/// <summary>
/// No-op store used when the Ai module is not registered, and the Board module's own default registration
/// (the <see cref="NullAiAgentResolver"/> precedent).
/// <para>
/// Unlike that class, <b>nothing here throws</b>: "there is no knowledge base" and "the knowledge base has
/// nothing" are the same answer to the caller, and refusing to answer would break the board chat in
/// module-scoped harnesses for no benefit.
/// </para>
/// </summary>
public sealed class NullWorkspaceKnowledgeStore : IWorkspaceKnowledgeStore
{
    public Task<IReadOnlyList<KnowledgeChunk>> GetTopChunksAsync(
        Guid workspaceId,
        ReadOnlyMemory<float> queryVector,
        bool onlyReady,
        int maxDocuments,
        int maxChunks,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<KnowledgeChunk>>([]);
}
