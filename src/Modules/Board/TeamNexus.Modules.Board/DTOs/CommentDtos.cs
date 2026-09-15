namespace TeamNexus.Modules.Board.DTOs;

/// <summary>
/// A new comment on a task.
/// <para>
/// <see cref="MentionUserIds"/> is the Phase 12 §3 addition and carries the people the author tagged
/// with <c>@tên</c>. It is <b>explicit ids, never a re-parse of the text</b> (decision D11): the
/// server cannot tell "I meant this An" from a client inventing the name, so the client — which owns
/// the member list and the autocomplete — states the ids and the server only verifies that each one is
/// a human member of the workspace.
/// </para>
/// <para>
/// Appended with a default so every existing caller (tests, the AI agent's <c>PostComment</c>
/// applier) keeps compiling unchanged.
/// </para>
/// </summary>
public sealed record CreateCommentRequest(string Content, IReadOnlyList<Guid>? MentionUserIds = null);

public sealed record UpdateCommentRequest(string Content);

public sealed record CommentResponse(
    Guid Id,
    Guid TaskId,
    Guid AuthorId,
    string AuthorName,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
