namespace TeamNexus.Modules.Ai.DTOs;

/// <summary>
/// One knowledge document as the API returns it (Giai đoạn 14.7 Track B §B3).
/// <para>
/// Deliberately <b>no content</b>: the list is for managing a knowledge base, and shipping every document's full
/// text to render a table would make this the most expensive endpoint in the product. The status and chunk count
/// are what a human needs to see that indexing worked.
/// </para>
/// </summary>
/// <param name="Status">
/// <c>pending</c> | <c>ready</c> | <c>failed</c>. <c>deleted</c> documents are never listed, so the UI has no
/// fourth case to render.
/// </param>
/// <param name="Error">Why embedding failed; null unless <paramref name="Status"/> is <c>failed</c>.</param>
public sealed record KnowledgeDocumentResponse(
    Guid Id,
    string Title,
    string? SourceUrl,
    string Status,
    int ChunkCount,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// The knowledge list plus the switch that governs it. <c>Enabled = false</c> is not an error: it means
/// <c>Jev:RagEnabled</c> is off, so the AI is not reading this knowledge base and the UI must say so rather than
/// present an empty list as if the workspace had nothing.
/// </summary>
public sealed record KnowledgeDocumentListResponse(
    bool Enabled,
    IReadOnlyList<KnowledgeDocumentResponse> Items);

/// <summary>
/// Body of <c>POST /api/workspaces/{id}/knowledge/documents</c>. Validated before the accountability row is
/// written, so a 400 never leaves a Pending action for a document that could not be applied.
/// </summary>
public sealed record CreateKnowledgeDocumentRequest(
    string? Title,
    string? SourceUrl,
    string? Content);
