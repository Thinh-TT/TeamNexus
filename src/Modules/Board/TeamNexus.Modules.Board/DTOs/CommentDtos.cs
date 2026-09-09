namespace TeamNexus.Modules.Board.DTOs;

public sealed record CreateCommentRequest(string Content);

public sealed record UpdateCommentRequest(string Content);

public sealed record CommentResponse(
    Guid Id,
    Guid TaskId,
    Guid AuthorId,
    string AuthorName,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
