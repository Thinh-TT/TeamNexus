using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// The AI Task Chat (Phase 14 §2.2): a task-scoped, streaming question/answer session plus the one
/// write path it has — saving an answer as a comment through the Accountability Layer.
/// <para>
/// <b>Read-only by design.</b> Streaming an answer writes nothing. The only thing that ever writes is
/// <see cref="SaveAnswerAsync"/>, and even that only creates a <c>Pending</c> log: a manager still has
/// to approve it. That is what keeps the Accountability Layer meaningful — auto-pending every chat turn
/// would bury the history drawer under questions the user asked out of curiosity.
/// </para>
/// </summary>
public interface IAiChatService
{
    /// <summary>
    /// The model name reported in the opening <c>meta</c> frame ("deepseek-chat" or the offline
    /// provider's type name). Exposed so the endpoint can build that frame without reaching into
    /// <c>DeepSeekOptions</c> itself.
    /// </summary>
    string ModelName { get; }

    /// <summary>
    /// Runs <b>every</b> guard eagerly (503 disabled · 404 unknown/soft-deleted task · 403 non-member ·
    /// 400 invalid transcript) and returns the prepared prompt. Because it is awaited before the HTTP
    /// response starts, those failures surface as real JSON status codes.
    /// </summary>
    Task<AiChatPreparedRequest> PrepareAsync(
        Guid taskId,
        AiChatSendRequest request,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Streams the answer for a prepared request as <b>already-rendered SSE frames</b>. From the first
    /// frame on the response has started, so a provider failure is delivered as an <c>error</c> frame
    /// rather than an exception.
    /// </summary>
    IAsyncEnumerable<string> StreamPreparedAsync(
        AiChatPreparedRequest prepared,
        CancellationToken ct = default);

    /// <summary>
    /// Records the answer as a <c>Pending</c> <c>PostComment</c> action (Member+; 400 when the text is
    /// empty or longer than <c>AiChat:MaxAnswerChars</c>). Reuses the Phase 7 comment applier, so
    /// approve/reject/undo behave exactly as they already do.
    /// </summary>
    Task<AiActionLogResponse> SaveAnswerAsync(
        Guid taskId,
        SaveAiChatMessageRequest request,
        Guid userId,
        CancellationToken ct = default);
}

/// <summary>
/// A request that has already passed every guard and carries the composed prompt (Phase 14 §2.2).
/// Splitting validation from streaming is what lets the endpoint answer 503/404/403/400 with a real
/// status code while the success path stays a lazily-produced stream.
/// </summary>
public sealed record AiChatPreparedRequest(
    Guid TaskId,
    Guid BoardId,
    Guid WorkspaceId,
    string TaskTitle,
    int HistoryMessages,
    string SystemPrompt,
    IReadOnlyList<AiChatMessage> Messages,
    double Temperature,
    int MaxOutputTokens);

public sealed class AiChatService : IAiChatService
{
    /// <summary>Roles a client may send. <c>system</c> is server-owned and never accepted from outside.</summary>
    private static readonly string[] AllowedRoles = ["user", "assistant"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IAiStreamingProvider _provider;
    private readonly IAiActionService _actions;
    private readonly AiChatOptions _options;
    private readonly DeepSeekOptions _aiOptions;
    private readonly ILogger<AiChatService> _logger;

    public AiChatService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IAiStreamingProvider provider,
        IAiActionService actions,
        IOptions<AiChatOptions> options,
        IOptions<DeepSeekOptions> aiOptions,
        ILogger<AiChatService> logger)
    {
        _db = db;
        _access = access;
        _provider = provider;
        _actions = actions;
        _options = options.Value;
        _aiOptions = aiOptions.Value;
        _logger = logger;
    }

    private AiChatEffectiveOptions Effective => _options.Effective;

    public string ModelName => _aiOptions.HasApiKey ? _aiOptions.Model : _provider.GetType().Name;

    // =====================================================================
    // Guards (eager — a real status code is still possible here)
    // =====================================================================

    public async Task<AiChatPreparedRequest> PrepareAsync(
        Guid taskId,
        AiChatSendRequest request,
        Guid userId,
        CancellationToken ct = default)
    {
        // 1) Disabled ⇒ 503 BEFORE touching the database or the provider. A switched-off feature must
        //    not reveal which tasks exist, and must not spend a single token.
        if (!_options.Enabled)
        {
            throw new AiChatDisabledException();
        }

        var effective = Effective;

        // 2) Task (a soft-deleted task is filtered out of the query ⇒ 404) + board.
        var task = await _db.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == task.BoardId, ct)
            ?? throw new NotFoundException("Board not found.");

        // 3) Member+ of the owning workspace. A non-member gets 404 rather than 403, so the endpoint
        //    never confirms that a workspace exists (same rule as RequireMemberAsync everywhere else).
        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);

        // 4) Validate the transcript BEFORE spending anything. The client owns the history, so this is
        //    the only thing standing between a runaway tab and a runaway prompt.
        var messages = ValidateMessages(request, effective);

        // 5) Bounded task context (title/column/priority/due date + the newest comments).
        var context = await LoadContextAsync(task, board.Id, effective, ct);

        return new AiChatPreparedRequest(
            task.Id,
            board.Id,
            board.WorkspaceId,
            task.Title,
            messages.Count,
            AiChatPrompts.BuildSystemPrompt(AiChatPrompts.BuildContextBlock(context)),
            messages,
            effective.Temperature,
            effective.MaxOutputTokens);
    }

    // =====================================================================
    // Streaming (response already started — failures become frames)
    // =====================================================================

    public async IAsyncEnumerable<string> StreamPreparedAsync(
        AiChatPreparedRequest prepared,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);

        yield return AiChatSseWriter.Meta(new AiChatSseWriter.AiChatMeta(
            prepared.TaskId,
            prepared.BoardId,
            prepared.WorkspaceId,
            prepared.TaskTitle,
            prepared.HistoryMessages,
            ModelName));

        var streamRequest = new AiStreamRequest(
            prepared.SystemPrompt,
            prepared.Messages,
            prepared.Temperature,
            prepared.MaxOutputTokens);

        var answer = new StringBuilder();
        int? promptTokens = null;
        int? completionTokens = null;

        // The provider call is wrapped because its exception can only surface HERE, once the headers are
        // already on the wire: turning it into a JSON 502 is no longer possible, so it becomes an
        // `error` frame and the client keeps whatever text it already received.
        var enumerator = _provider.StreamAsync(streamRequest, ct).GetAsyncEnumerator(ct);

        // The provider exception is caught and held rather than yielded inside the catch: C# forbids
        // `yield` in a catch clause, and holding the message keeps the control flow flat and readable.
        string? streamError = null;
        var completed = false;

        try
        {
            while (!completed)
            {
                AiStreamChunk chunk;

                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        break;
                    }

                    chunk = enumerator.Current;
                }
                catch (AiProviderException ex)
                {
                    _logger.LogWarning(ex, "AI chat stream failed for task {TaskId}.", prepared.TaskId);
                    streamError = ex.Message;
                    break;
                }

                if (chunk.PromptTokens.HasValue)
                {
                    promptTokens = chunk.PromptTokens;
                }

                if (chunk.CompletionTokens.HasValue)
                {
                    completionTokens = chunk.CompletionTokens;
                }

                if (chunk.Done)
                {
                    completed = true;
                    break;
                }

                if (chunk.Delta is { Length: > 0 } delta)
                {
                    answer.Append(delta);
                    yield return AiChatSseWriter.Delta(delta);
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        if (streamError is not null)
        {
            // Delivered as a frame, not a 502: the status line was sent with the first `meta` frame.
            yield return AiChatSseWriter.Error(streamError);
            yield break;
        }

        _logger.LogInformation(
            "AI chat answered task {TaskId} with {Messages} history message(s), {Chars} char(s), "
            + "{PromptTokens}/{CompletionTokens} token(s).",
            prepared.TaskId,
            prepared.HistoryMessages,
            answer.Length,
            promptTokens,
            completionTokens);

        yield return AiChatSseWriter.Done(answer.ToString(), promptTokens, completionTokens);
    }

    // =====================================================================
    // Save the answer through the Accountability Layer
    // =====================================================================

    public async Task<AiActionLogResponse> SaveAnswerAsync(
        Guid taskId,
        SaveAiChatMessageRequest request,
        Guid userId,
        CancellationToken ct = default)
    {
        // Same switch as the streaming path: no 503 check would let a disabled feature still write.
        if (!_options.Enabled)
        {
            throw new AiChatDisabledException();
        }

        var content = request.Content?.Trim();

        if (string.IsNullOrEmpty(content))
        {
            throw new BadRequestException("Nội dung bình luận không được để trống.");
        }

        var maxAnswerChars = Effective.MaxAnswerChars;
        if (content.Length > maxAnswerChars)
        {
            // Never trust the client's disabled button: `task_comments.content` is capped at 2000 and a
            // longer body would be truncated silently, which is worse than a clear 400.
            throw new BadRequestException(
                $"Nội dung bình luận tối đa {maxAnswerChars} ký tự (nhận {content.Length}).");
        }

        var task = await _db.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == task.BoardId, ct)
            ?? throw new NotFoundException("Board not found.");

        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);

        // The AFTER snapshot is the pending comment, in the exact shape the Phase 7 PostComment applier
        // reads (`taskId` + `content`). Approving it posts the comment with the SAVING USER as the author
        // (not the AI) and undo soft-deletes it — behaviour already covered by the Phase 7 suites.
        var afterSnapshot = JsonSerializer.Serialize(new { taskId, content }, Json);

        var basis = JsonSerializer.Serialize(new
        {
            source = "AiTaskChat",
            taskId,
            boardId = board.Id,
            contentLength = content.Length,
            requestedAt = DateTimeOffset.UtcNow,
        }, Json);

        // Human-initiated request: NOT RequestAgentOutputAsync, which asserts the task is assigned to
        // the workspace's AI Agent and would 403 every save a normal member makes.
        var log = await _actions.RequestChatCommentAsync(
            taskId,
            userId,
            afterSnapshot,
            basis,
            ct);

        _logger.LogInformation(
            "AI chat answer for task {TaskId} recorded as Pending action {LogId} by user {UserId}.",
            taskId,
            log.Id,
            userId);

        return log;
    }

    // =====================================================================
    // Guards + context (pure / bounded)
    // =====================================================================

    /// <summary>
    /// Turns the client transcript into provider messages, or throws <see cref="BadRequestException"/>
    /// (400) explaining exactly which limit was broken. Every rule here exists to bound cost before the
    /// provider is called at all.
    /// </summary>
    private static List<AiChatMessage> ValidateMessages(
        AiChatSendRequest request,
        AiChatEffectiveOptions effective)
    {
        var messages = request.Messages;

        if (messages is null || messages.Count == 0)
        {
            throw new BadRequestException("Cần ít nhất một tin nhắn để hỏi AI.");
        }

        if (messages.Count > effective.MaxHistoryMessages)
        {
            throw new BadRequestException(
                $"Lịch sử hội thoại tối đa {effective.MaxHistoryMessages} tin nhắn (nhận {messages.Count}).");
        }

        var validated = new List<AiChatMessage>(messages.Count);
        var totalChars = 0;

        foreach (var message in messages)
        {
            if (message is null || string.IsNullOrWhiteSpace(message.Role)
                || !AllowedRoles.Contains(message.Role, StringComparer.OrdinalIgnoreCase))
            {
                throw new BadRequestException(
                    "Vai trò tin nhắn phải là 'user' hoặc 'assistant'.");
            }

            var content = message.Content?.Trim();
            if (string.IsNullOrEmpty(content))
            {
                throw new BadRequestException("Tin nhắn không được để trống.");
            }

            totalChars += content.Length;
            validated.Add(new AiChatMessage(message.Role.ToLowerInvariant(), content));
        }

        if (totalChars > effective.MaxHistoryChars)
        {
            throw new BadRequestException(
                $"Tổng nội dung hội thoại tối đa {effective.MaxHistoryChars} ký tự (nhận {totalChars}).");
        }

        return validated;
    }

    /// <summary>
    /// Reads the bounded task context: description excerpt, column/board names, assignee and the newest
    /// comments (already shortened per comment). The overall block is capped, so no combination of a
    /// long description and many long comments can blow the prompt budget.
    /// </summary>
    private async Task<AiChatTaskContext> LoadContextAsync(
        Persistence.Data.Entities.BoardTask task,
        Guid boardId,
        AiChatEffectiveOptions effective,
        CancellationToken ct)
    {
        var boardName = await _db.Boards
            .AsNoTracking()
            .Where(b => b.Id == boardId)
            .Select(b => b.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var columnName = await _db.BoardColumns
            .AsNoTracking()
            .Where(c => c.Id == task.ColumnId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var assigneeName = task.AssigneeId is null
            ? null
            : await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == task.AssigneeId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct);

        var comments = new List<AiChatContextComment>();

        if (effective.MaxCommentsInContext > 0)
        {
            var rows = await _db.TaskComments
                .AsNoTracking()
                .Where(c => c.TaskId == task.Id)
                .OrderByDescending(c => c.CreatedAt)
                .Take(effective.MaxCommentsInContext)
                .Select(c => new
                {
                    Author = c.Author != null ? c.Author.DisplayName : "(không rõ)",
                    c.CreatedAt,
                    Content = c.Content ?? string.Empty,
                })
                .ToListAsync(ct);

            // Newest-first from the database, oldest-first for the prompt (same convention as the Agent).
            rows.Reverse();

            comments.AddRange(rows.Select(r => new AiChatContextComment(
                r.Author,
                Cap(r.Content, effective.MaxCommentCharsInContext),
                r.CreatedAt)));
        }

        return new AiChatTaskContext(
            task.Id,
            task.Title,
            boardName,
            columnName,
            Cap(task.Description, effective.MaxTaskContextChars),
            task.Priority?.ToString(),
            task.DueDate,
            assigneeName,
            comments);
    }

    /// <summary>
    /// Truncates a prompt value, marking the cut with an ellipsis so the model can tell an excerpt from a
    /// complete text. A null/blank value becomes <see cref="string.Empty"/> — never null — because a
    /// context field that is "missing" and one that is "null" are indistinguishable to the model anyway,
    /// and carrying nullness into the prompt builder only invites null-reference bugs.
    /// </summary>
    private static string Cap(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength] + "…";
    }
}
