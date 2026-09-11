using System.Text.Json;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Ai.Services.Agent;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Appliers;

/// <summary>
/// Applier for <see cref="AiActionTypes.PostComment"/> (Phase 7 §4.7): posts the agent's short result
/// as a task comment through <see cref="ICommentService"/>.
/// <para>
/// Two deliberate choices:
/// <list type="bullet">
///   <item>The comment's author is <c>log.RequestedByUserId</c> (the agent), <b>not</b>
///   <c>ctx.ActingUserId</c> (the manager who approved). Phase 4's context carries the caller, so
///   using it here would silently attribute the AI's work to the human who reviewed it.</item>
///   <item>Going through the service buys the Phase 5 activity log, the <c>CommentAdded</c> SignalR
///   event and the <c>authorName</c> resolution for free (S15).</item>
/// </list>
/// </para>
/// </summary>
public sealed class PostCommentApplier : IAiActionApplier
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ICommentService _comments;
    private readonly ILogger<PostCommentApplier> _logger;

    public PostCommentApplier(ICommentService comments, ILogger<PostCommentApplier> logger)
    {
        _comments = comments;
        _logger = logger;
    }

    public string ActionType => AiActionTypes.PostComment;

    public async Task<AiActionAppliedResult> ApplyAsync(
        AiActionLog log, AiActionContext ctx, CancellationToken ct = default)
    {
        var snapshot = Parse(log);

        var comment = await _comments.CreateCommentAsync(
            snapshot.TaskId,
            new CreateCommentRequest(snapshot.Content),
            // The AGENT is the author — see the class remarks.
            log.RequestedByUserId,
            ct);

        _logger.LogInformation(
            "PostComment applied (log {LogId}, task {TaskId}, comment {CommentId}, author {AuthorId}).",
            log.Id, snapshot.TaskId, comment.Id, log.RequestedByUserId);

        return new AiActionAppliedResult(
            AiEntityTypes.Task,
            snapshot.TaskId,
            [],
            [],
            [],
            CreatedCommentId: comment.Id);
    }

    public async Task<IReadOnlyList<string>> UndoAsync(
        AiActionLog log, AiActionContext ctx, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var commentId = AiActionService.ReadGuid(log.AppliedSnapshot, "createdCommentId");

        if (commentId is null)
        {
            warnings.Add("The action has no createdCommentId; nothing to remove.");
            return warnings;
        }

        try
        {
            // ctx.ActingUserId is the deciding manager: CommentService authorizes the author (the
            // agent, who cannot log in) OR any Manager of the workspace, which is exactly this case.
            await _comments.DeleteCommentAsync(commentId.Value, ctx.ActingUserId, ct);
        }
        catch (NotFoundException)
        {
            warnings.Add($"Comment {commentId.Value} no longer exists; nothing to remove.");
        }

        return warnings;
    }

    private static AgentCommentSnapshot Parse(AiActionLog log)
    {
        if (string.IsNullOrWhiteSpace(log.AfterSnapshot))
        {
            throw new BadRequestException("The AI action has no after_snapshot to apply.");
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<AgentCommentSnapshot>(log.AfterSnapshot, Json)
                           ?? throw new BadRequestException("The AI action after_snapshot is empty.");

            if (snapshot.TaskId == Guid.Empty || string.IsNullOrWhiteSpace(snapshot.Content))
            {
                throw new BadRequestException("The AI action after_snapshot is missing taskId or content.");
            }

            return snapshot;
        }
        catch (JsonException ex)
        {
            throw new BadRequestException($"The AI action after_snapshot is not valid JSON: {ex.Message}");
        }
    }
}
