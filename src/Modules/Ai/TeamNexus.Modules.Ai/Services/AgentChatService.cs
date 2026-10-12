using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// The board-scoped AI Agent Chat (Phase 14.5 §B): one conversation per board, several attached tasks,
/// streamed answers, and a transcript that is actually stored.
/// <para>
/// <b>What it replaces, and what it keeps.</b> The roadmap's "gộp AI Agent + Hỏi AI thành AI Agent Chat
/// tổng của board" is satisfied by <i>adding</i> a surface, not by deleting one: the in-task tab keeps
/// working (its contract and tests from Phase 14 are untouched) and gains a clearer label, while this
/// service becomes the board's entry point. Both share the streaming port, the SSE frame writer and the
/// transcript validator, so there is exactly one chat transport in the codebase.
/// </para>
/// <para>
/// <b>Write discipline is unchanged.</b> Streaming an answer writes only chat history. A model's proposal
/// is data in the answer; it becomes an <c>ai_action_logs</c> row in <c>Pending</c> only when a human
/// submits it, and only a Manager can approve it. That keeps the Phase 4 invariant intact — no business
/// data changes without a decision — while still letting the agent be genuinely useful.
/// </para>
/// </summary>
public interface IAgentChatService
{
    /// <summary>The model name reported in the opening <c>meta</c> frame.</summary>
    string ModelName { get; }

    /// <summary>
    /// Runs <b>every</b> guard eagerly (503 disabled · 404 unknown board or non-member · 400 bad
    /// transcript or bad attachment list) and returns the prepared prompt. Awaited before the HTTP
    /// response starts, so those failures are real status codes rather than frames.
    /// </summary>
    Task<AgentChatPreparedRequest> PrepareAsync(
        Guid boardId,
        AgentChatSendRequest request,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Streams the answer as already-rendered SSE frames. From the first frame on the response has
    /// started, so a provider failure leaves as an <c>error</c> frame instead of an exception.
    /// </summary>
    IAsyncEnumerable<string> StreamPreparedAsync(
        AgentChatPreparedRequest prepared,
        CancellationToken ct = default);

    /// <summary>
    /// Stored turns of one board, oldest first. <paramref name="before"/> is a keyset cursor
    /// (<c>createdAt</c> of the oldest row already loaded) for "load older".
    /// </summary>
    Task<IReadOnlyList<AgentChatMessageResponse>> ListMessagesAsync(
        Guid boardId,
        DateTimeOffset? before,
        int take,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Records the answer as a <c>Pending</c> <c>PostComment</c> on one of the attached tasks. Reuses the
    /// Phase 14 human path (<c>RequestChatCommentAsync</c>) so approve/reject/undo behave identically.
    /// </summary>
    Task<AiActionLogResponse> SaveAnswerAsync(
        Guid boardId,
        SaveAgentChatAnswerRequest request,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Turns one proposal from an answer into a <c>Pending</c> action. Re-validates the proposal from the
    /// raw JSON — the same untrusted-input boundary as the model's own output — because the client could
    /// otherwise submit anything it liked.
    /// </summary>
    Task<AiActionLogResponse> SubmitProposalAsync(
        Guid boardId,
        SubmitAgentChatProposalRequest request,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>Persists both turns of a completed exchange, so the transcript survives the session.</summary>
    Task SaveTranscriptAsync(
        AgentChatPreparedRequest prepared,
        string question,
        string answer,
        CancellationToken ct = default);
}

/// <summary>
/// A request that has already passed every guard and carries the composed prompt (Phase 14.5 §B).
/// Splitting validation from streaming is what lets 503/404/403/400 answer with a real status code while
/// the success path stays a lazily-produced stream — the Phase 14 D4 shape, reused unchanged.
/// </summary>
public sealed record AgentChatPreparedRequest(
    Guid BoardId,
    Guid WorkspaceId,
    IReadOnlyList<AgentChatContextTask> AttachedTasks,
    int HistoryMessages,
    string SystemPrompt,
    IReadOnlyList<AiChatMessage> Messages,
    double Temperature,
    int MaxOutputTokens);

/// <summary>Body of <c>POST /api/boards/{boardId}/agent-chat/proposal/confirm</c>.</summary>
/// <param name="ProposalJson">The raw JSON block, exactly as the client received it.</param>
/// <param name="AttachedTaskIds">
/// The tasks the user had attached when they asked. Re-sent because the server must re-check the model's
/// claim against the same scope, and the server keeps no per-request session state.
/// </param>
public sealed record SubmitAgentChatProposalRequest(
    string? ProposalJson,
    IReadOnlyList<AgentChatAttachmentRequest>? AttachedTaskIds);

public sealed class AgentChatService : IAgentChatService
{
    /// <summary>Default page size for the stored transcript.</summary>
    public const int DefaultMessageTake = AgentChatTranscriptQuery.DefaultTake;

    /// <summary>Hard ceiling on a transcript page, so one request cannot pull the whole history.</summary>
    public const int MaxMessageTake = AgentChatTranscriptQuery.MaxTake;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IAiStreamingProvider _provider;
    private readonly IAiActionService _actions;
    private readonly AgentChatOptions _options;
    private readonly DeepSeekOptions _aiOptions;
    private readonly ILogger<AgentChatService> _logger;

    public AgentChatService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IAiStreamingProvider provider,
        IAiActionService actions,
        IOptions<AgentChatOptions> options,
        IOptions<DeepSeekOptions> aiOptions,
        ILogger<AgentChatService> logger)
    {
        _db = db;
        _access = access;
        _provider = provider;
        _actions = actions;
        _options = options.Value;
        _aiOptions = aiOptions.Value;
        _logger = logger;
    }

    private AgentChatEffectiveOptions Effective => _options.Effective;

    public string ModelName => _aiOptions.HasApiKey ? _aiOptions.Model : _provider.GetType().Name;

    // =====================================================================
    // Guards (eager — a real status code is still possible here)
    // =====================================================================

    public async Task<AgentChatPreparedRequest> PrepareAsync(
        Guid boardId,
        AgentChatSendRequest request,
        Guid userId,
        CancellationToken ct = default)
    {
        // Disabled ⇒ 503 BEFORE touching the database or the provider: a switched-off feature must not
        // reveal which boards exist and must not spend a token.
        if (!_options.Enabled)
        {
            throw new AgentChatDisabledException();
        }

        var effective = Effective;

        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == boardId, ct)
            ?? throw new NotFoundException("Board not found.");

        // Member+ of the owning workspace; a non-member gets 404 rather than 403, so the endpoint never
        // confirms that a workspace exists.
        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);

        var messages = AiChatTranscriptValidator.Validate(
            request.Messages,
            effective.MaxHistoryMessages,
            effective.MaxHistoryChars,
            effective.MaxMessageChars);

        var attached = await LoadAttachedTasksAsync(
            board.Id, board.WorkspaceId, request.AttachedTaskIds, effective, ct);

        var context = await LoadBoardContextAsync(board, attached, effective, ct);

        return new AgentChatPreparedRequest(
            board.Id,
            board.WorkspaceId,
            attached,
            messages.Count,
            AgentChatPrompts.BuildSystemPrompt(AgentChatPrompts.BuildContextBlock(context)),
            messages,
            effective.Temperature,
            effective.MaxOutputTokens);
    }

    // =====================================================================
    // Streaming (response already started — failures become frames)
    // =====================================================================

    public async IAsyncEnumerable<string> StreamPreparedAsync(
        AgentChatPreparedRequest prepared,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);

        yield return AiChatSseWriter.Meta(new AiChatSseWriter.AiChatMeta(
            // The frame record is shared with the task chat, so the field NAMES are fixed. For a board chat
            // the slots are filled as: TaskId = the board being discussed (there is no task),
            // BoardId = the board, TaskTitle = a human-readable description of what is in scope.
            TaskId: prepared.BoardId,
            BoardId: prepared.BoardId,
            WorkspaceId: prepared.WorkspaceId,
            TaskTitle: $"{prepared.AttachedTasks.Count} thẻ đang gắn",
            HistoryMessages: prepared.HistoryMessages,
            Model: ModelName));

        var streamRequest = new AiStreamRequest(
            prepared.SystemPrompt,
            prepared.Messages,
            prepared.Temperature,
            prepared.MaxOutputTokens);

        var answer = new StringBuilder();
        int? promptTokens = null;
        int? completionTokens = null;

        var enumerator = _provider.StreamAsync(streamRequest, ct).GetAsyncEnumerator(ct);

        // The provider exception is caught and held rather than yielded inside the catch: C# forbids
        // `yield` in a catch clause, and holding the message keeps the control flow flat.
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
                    _logger.LogWarning(ex, "Agent chat stream failed for board {BoardId}.", prepared.BoardId);
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

                // Append the delta BEFORE looking at `Done`. The two are not mutually exclusive: a provider
                // may put text in the same chunk that terminates the stream (the offline fake does exactly
                // that, and a real one may too), and reading `Done` first silently dropped that text — which
                // broke the contract `done.answer == Σ delta` and truncated every answer by one chunk.
                if (chunk.Delta is { Length: > 0 } delta)
                {
                    answer.Append(delta);

                    // Yield per frame, never buffered: an SSE endpoint that collects everything first is
                    // streaming in name only (the Phase 14 rule, restated because it is easy to regress).
                    yield return AiChatSseWriter.Delta(delta);
                }

                if (chunk.Done)
                {
                    completed = true;
                    break;
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        if (streamError is not null)
        {
            yield return AiChatSseWriter.Error(streamError);
            yield break;
        }

        var fullAnswer = answer.ToString();

        // The transcript is stored WITHOUT the answer's JSON block: the proposal belongs in the action log
        // (where a Manager reads it as a preview), while the transcript is the conversation a human reads.
        // Keeping the raw block here would show every user a wall of JSON they cannot act on, and
        // re-parsing it on reload would offer a "submit" button whose scope check no longer has the
        // attachments that justified it.
        await PersistTranscriptBestEffortAsync(prepared, fullAnswer, ct);

        _logger.LogInformation(
            "Agent chat answered board {BoardId} with {Tasks} attached task(s), {Messages} history message(s), "
            + "{Chars} char(s), {PromptTokens}/{CompletionTokens} token(s).",
            prepared.BoardId,
            prepared.AttachedTasks.Count,
            prepared.HistoryMessages,
            answer.Length,
            promptTokens,
            completionTokens);

        // The frame still carries the FULL answer (the client needs the block to offer the proposal).
        yield return AiChatSseWriter.Done(fullAnswer, promptTokens, completionTokens);
    }

    // =====================================================================
    // Transcript
    // =====================================================================

    public async Task<IReadOnlyList<AgentChatMessageResponse>> ListMessagesAsync(
        Guid boardId,
        DateTimeOffset? before,
        int take,
        Guid userId,
        CancellationToken ct = default)
    {
        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == boardId, ct)
            ?? throw new NotFoundException("Board not found.");

        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);

        var limit = AgentChatTranscriptQuery.ClampTake(take);

        var rows = await AgentChatTranscriptQuery.NewestPageAsync(
            _db.AgentChatMessages.Where(m => m.BoardId == boardId),
            before,
            limit,
            ct);

        return rows.Select(AgentChatTranscriptMapper.ToResponse).ToList();
    }

    public async Task SaveTranscriptAsync(
        AgentChatPreparedRequest prepared,
        string question,
        string answer,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);

        // Bounded here, not trusted from the caller: the two strings are model/user text and this is the
        // last place before a database write.
        var questionText = Cap(question, Effective.MaxMessageChars);
        var answerText = Cap(answer, Effective.MaxMessageChars);

        if (string.IsNullOrWhiteSpace(questionText) && string.IsNullOrWhiteSpace(answerText))
        {
            return;
        }

        var attachments = JsonSerializer.Serialize(
            prepared.AttachedTasks.Select(t => new { taskId = t.TaskId, title = t.Title }), Json);

        var rows = new List<AgentChatMessage>();

        if (!string.IsNullOrEmpty(questionText))
        {
            rows.Add(new AgentChatMessage
            {
                BoardId = prepared.BoardId,
                WorkspaceId = prepared.WorkspaceId,
                Role = AgentChatRole.User,
                Content = questionText,
                Attachments = attachments,
            });
        }

        if (!string.IsNullOrEmpty(answerText))
        {
            rows.Add(new AgentChatMessage
            {
                BoardId = prepared.BoardId,
                WorkspaceId = prepared.WorkspaceId,
                Role = AgentChatRole.Assistant,
                Content = answerText,
                Attachments = attachments,
            });
        }

        _db.AgentChatMessages.AddRange(rows);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Agent chat transcript for board {BoardId}: {Rows} row(s) stored.",
            prepared.BoardId,
            rows.Count);
    }

    /// <summary>
    /// Stores the exchange, swallowing failure.
    /// <para>
    /// <b>Why best-effort.</b> This runs <i>after</i> the answer has already reached the client: the
    /// response status line and every frame are on the wire, so an exception here could not become a 400 —
    /// it would only abort a stream the user has effectively already received. Losing one stored turn is a
    /// re-load inconvenience; killing a successful answer is a broken feature. The same reasoning
    /// (inherited from <c>NotificationWriter</c>) is why the failure is logged rather than raised.
    /// </para>
    /// </summary>
    private async Task PersistTranscriptBestEffortAsync(
        AgentChatPreparedRequest prepared,
        string fullAnswer,
        CancellationToken ct)
    {
        try
        {
            // `prepared.Messages` is the client's replay of the conversation, so the last user turn IS the
            // question that produced this answer — no extra request field is needed to know it.
            var question = prepared.Messages.LastOrDefault(
                m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content
                ?? string.Empty;

            // Phase 14.6 §A2: the stored prose is sanitized, not just prompted against. The parser has
            // already removed the JSON block (whose ids are legitimate and must survive), so whatever is
            // left is conversation — and a raw Guid there is a leak that a reload would replay verbatim.
            // The live stream is filtered on the client for the same reason; this is the half that keeps a
            // reload from resurrecting what was hidden.
            var visibleAnswer = AiChatAnswerSanitizer.RedactIdentifiers(
                AgentChatProposalParser.Parse(fullAnswer).VisibleText);

            await SaveTranscriptAsync(prepared, question, visibleAnswer, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not store the agent chat transcript for board {BoardId}; the answer was already "
                + "streamed to the client and is unaffected.",
                prepared.BoardId);
        }
    }

    // =====================================================================
    // The two human-approved write paths
    // =====================================================================

    public async Task<AiActionLogResponse> SaveAnswerAsync(
        Guid boardId,
        SaveAgentChatAnswerRequest request,
        Guid userId,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            throw new AgentChatDisabledException();
        }

        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == boardId, ct)
            ?? throw new NotFoundException("Board not found.");

        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);

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

        // The comment must land on a task of THIS board. Without this check a caller could post the answer
        // as a comment on any task they can name — the explanation is board-scoped, the write must be too.
        var targetTaskId = await ResolveTargetTaskAsync(board.Id, request.TaskId, ct);

        var afterSnapshot = JsonSerializer.Serialize(new { taskId = targetTaskId, content }, Json);

        var basis = JsonSerializer.Serialize(new
        {
            source = "AgentChat",
            boardId,
            taskId = targetTaskId,
            contentLength = content.Length,
            requestedAt = DateTimeOffset.UtcNow,
        }, Json);

        var log = await _actions.RequestChatCommentAsync(targetTaskId, userId, afterSnapshot, basis, ct);

        _logger.LogInformation(
            "Agent chat answer for board {BoardId} recorded as Pending action {LogId} by user {UserId}.",
            boardId,
            log.Id,
            userId);

        return log;
    }

    public async Task<AiActionLogResponse> SubmitProposalAsync(
        Guid boardId,
        SubmitAgentChatProposalRequest request,
        Guid userId,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            throw new AgentChatDisabledException();
        }

        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == boardId, ct)
            ?? throw new NotFoundException("Board not found.");

        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);

        // Unwrap HERE, before validating: the client kept the whole fenced block, whose outer object is the
        // documented envelope `{ "proposal": { … } }` while the validator works on the proposal itself. One
        // shared helper for both directions means the object the UI previewed is the object that is checked.
        var proposalJson = AgentChatProposalParser.UnwrapEnvelope(request.ProposalJson ?? string.Empty);

        // Re-derive the scope from the database rather than trusting the client's list: the attachment
        // ids arrive in the request body, so they are as untrusted as the model's JSON.
        var attachedIds = await LoadAttachedTaskIdsAsync(board.Id, request.AttachedTaskIds, ct);

        var proposal = await NormalizeProposalAsync(
            board.Id, board.WorkspaceId, proposalJson, attachedIds, ct);

        var afterSnapshot = JsonSerializer.Serialize(ToSnapshot(proposal), Json);

        var basis = JsonSerializer.Serialize(new
        {
            source = "AgentChat",
            boardId,
            kind = proposal.Kind,
            attachedTaskCount = attachedIds.Count,
            requestedAt = DateTimeOffset.UtcNow,
        }, Json);

        // One service call per kind, but all of them land in the same Pending log shape: the Accountability
        // Layer stays the single gateway, and the action type is what routes it to an applier.
        var log = proposal.Kind switch
        {
            AgentChatProposalKinds.CreateSubtasks => await _actions.RequestCreateSubtasksAsync(
                board.Id,
                new ConfirmSmartSetupRequest(
                    // `Description` is the Smart Setup contract's "basis of the action" text; a chat
                    // proposal has no user description to hand over, so the validated summary is the
                    // honest stand-in and it keeps the stored basis readable.
                    Description: proposal.Summary,
                    Summary: proposal.Summary,
                    Tasks: proposal.Payload.Tasks ?? [],
                    ColumnId: proposal.Payload.ColumnId),
                userId,
                ct),

            AgentChatProposalKinds.UpdateTaskFields => await _actions.RequestUpdateTaskFieldsAsync(
                board.Id, proposal.Payload.TaskId!.Value, afterSnapshot, basis, userId, ct),

            AgentChatProposalKinds.MoveTaskToColumn => await _actions.RequestMoveTaskAsync(
                board.Id, proposal.Payload.TaskId!.Value, afterSnapshot, basis, userId, ct),

            _ => throw new BadRequestException($"Loại đề xuất không được hỗ trợ: '{proposal.Kind}'."),
        };

        _logger.LogInformation(
            "Agent chat proposal {Kind} for board {BoardId} recorded as Pending action {LogId} by user {UserId}.",
            proposal.Kind,
            boardId,
            log.Id,
            userId);

        return log;
    }

    // =====================================================================
    // Context loading (bounded)
    // =====================================================================

    /// <summary>
    /// Reads the attached tasks with everything the prompt needs, honouring both the task-count cap and
    /// the <b>total</b> character budget. The running total is what enforces the second: a user can attach
    /// eight long tasks, and it is the characters — not the count — that decide what the prompt costs.
    /// Unknown or foreign task ids are a <b>400</b> rather than a silent skip, because "the AI ignored the
    /// card I dragged in" is indistinguishable from a bug otherwise.
    /// </summary>
    private async Task<IReadOnlyList<AgentChatContextTask>> LoadAttachedTasksAsync(
        Guid boardId,
        Guid workspaceId,
        IReadOnlyList<AgentChatAttachmentRequest>? request,
        AgentChatEffectiveOptions effective,
        CancellationToken ct)
    {
        var ids = (request ?? [])
            .Where(a => a is not null && a.TaskId != Guid.Empty)
            .Select(a => a.TaskId)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        if (ids.Count > effective.MaxAttachedTasks)
        {
            throw new BadRequestException(
                $"Chỉ được gắn tối đa {effective.MaxAttachedTasks} thẻ cho một lượt hỏi (nhận {ids.Count}).");
        }

        var tasks = await _db.Tasks
            .AsNoTracking()
            .Where(t => ids.Contains(t.Id) && t.BoardId == boardId)
            .OrderBy(t => t.Position)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.ColumnId,
                t.Description,
                t.Priority,
                t.DueDate,
                t.AssigneeId,
            })
            .ToListAsync(ct);

        if (tasks.Count != ids.Count)
        {
            throw new BadRequestException(
                "Có thẻ được gắn không thuộc bảng này hoặc đã bị xoá. Hãy bỏ thẻ đó và thử lại.");
        }

        var columnNames = await _db.BoardColumns
            .AsNoTracking()
            .Where(c => c.BoardId == boardId)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var assigneeNames = await LoadAssigneeNamesAsync(
            tasks.Where(t => t.AssigneeId.HasValue).Select(t => t.AssigneeId!.Value).Distinct().ToList(), ct);

        var result = new List<AgentChatContextTask>(tasks.Count);
        var usedChars = 0;

        foreach (var task in tasks)
        {
            var description = Cap(task.Description, effective.MaxTaskContextChars);
            var comments = await LoadCommentsAsync(task.Id, effective, ct);

            var cost = description.Length + task.Title.Length + comments.Sum(c => c.Content.Length);

            // The total budget is checked before adding, and a task that would blow it is dropped whole
            // (rather than truncated to nothing) — a half-described card misleads the model more than an
            // absent one, and the count of what actually went in is reported in the `meta` frame.
            if (usedChars + cost > effective.MaxTotalContextChars && result.Count > 0)
            {
                _logger.LogInformation(
                    "Agent chat context budget ({Budget} chars) reached; {Dropped} attached task(s) left out.",
                    effective.MaxTotalContextChars,
                    tasks.Count - result.Count);
                break;
            }

            usedChars += cost;

            result.Add(new AgentChatContextTask(
                task.Id,
                task.Title,
                columnNames.TryGetValue(task.ColumnId, out var columnName) ? columnName : string.Empty,
                description,
                task.Priority?.ToString(),
                task.DueDate,
                task.AssigneeId is { } assigneeId && assigneeNames.TryGetValue(assigneeId, out var name)
                    ? name
                    : null,
                comments));
        }

        return result;
    }

    /// <summary>Ids only, used by the proposal path where the full context block is not built.</summary>
    private async Task<IReadOnlyList<Guid>> LoadAttachedTaskIdsAsync(
        Guid boardId,
        IReadOnlyList<AgentChatAttachmentRequest>? request,
        CancellationToken ct)
    {
        var ids = (request ?? [])
            .Where(a => a is not null && a.TaskId != Guid.Empty)
            .Select(a => a.TaskId)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        var effective = Effective;

        if (ids.Count > effective.MaxAttachedTasks)
        {
            throw new BadRequestException(
                $"Chỉ được gắn tối đa {effective.MaxAttachedTasks} thẻ cho một lượt hỏi (nhận {ids.Count}).");
        }

        var found = await _db.Tasks
            .AsNoTracking()
            .Where(t => ids.Contains(t.Id) && t.BoardId == boardId)
            .Select(t => t.Id)
            .ToListAsync(ct);

        if (found.Count != ids.Count)
        {
            throw new BadRequestException(
                "Có thẻ được gắn không thuộc bảng này hoặc đã bị xoá. Hãy bỏ thẻ đó và thử lại.");
        }

        return found;
    }

    private async Task<AgentChatBoardContext> LoadBoardContextAsync(
        Persistence.Data.Entities.Board board,
        IReadOnlyList<AgentChatContextTask> attached,
        AgentChatEffectiveOptions effective,
        CancellationToken ct)
    {
        var columns = await _db.BoardColumns
            .AsNoTracking()
            .Where(c => c.BoardId == board.Id)
            .OrderBy(c => c.Position)
            .Select(c => new AgentChatContextColumn(c.Id, c.Name, c.IsDone))
            .ToListAsync(ct);

        // Human members only: assigning a task to the AI Agent is how the Phase 7 executor is triggered,
        // and that is a deliberate UI action — not something a chat proposal should smuggle in.
        var members = await _db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.WorkspaceId == board.WorkspaceId && m.MemberType == MemberType.Human)
            .OrderBy(m => m.JoinedAt)
            .Join(
                _db.Users.AsNoTracking(),
                m => m.UserId,
                u => u.Id,
                (m, u) => new AgentChatContextMember(u.Id, u.DisplayName ?? string.Empty))
            .ToListAsync(ct);

        // Phase 14.6 §A4: with nothing attached the prompt used to carry column NAMES and nothing else, so
        // "how is Sprint 1 going?" was answered with an empty refusal on a board full of cards. The digest
        // is read ONLY in that case — an attachment list already describes the tasks the user cares about,
        // and adding board-wide statistics on top would spend tokens on something nobody asked about.
        var digest = attached.Count == 0
            ? await LoadDigestAsync(board.Id, columns, effective, ct)
            : null;

        return new AgentChatBoardContext(
            board.Name,
            Cap(board.Description, Math.Min(effective.MaxTotalContextChars, 1000)),
            columns,
            attached,
            members,
            digest);
    }

    /// <summary>
    /// Reads the data behind the Lightweight Board Digest (Phase 14.6 §A4): exact card counts per column plus
    /// the cards worth naming first.
    /// <para>
    /// <b>Why ranking happens in memory.</b> <c>tasks.priority</c> is a text column, so ordering by it in SQL
    /// would sort alphabetically — <c>High, Low, Medium, Urgent</c> — which is not the severity order and
    /// would make the digest name the <i>least</i> urgent cards. A bounded page (see
    /// <see cref="DigestCandidateLimit"/>) is therefore read and ranked with the real enum.
    /// </para>
    /// <para>
    /// <b>Why the counts are a second query.</b> They must be exact even when the board holds more cards
    /// than the candidate page: a digest that says "3 cards" about a 300-card board would be worse than no
    /// digest at all.
    /// </para>
    /// </summary>
    private async Task<AgentChatDigest> LoadDigestAsync(
        Guid boardId,
        IReadOnlyList<AgentChatContextColumn> columns,
        AgentChatEffectiveOptions effective,
        CancellationToken ct)
    {
        var counts = await _db.Tasks
            .AsNoTracking()
            .Where(t => t.BoardId == boardId)
            .GroupBy(t => t.ColumnId)
            .Select(g => new { ColumnId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var countByColumn = counts.ToDictionary(c => c.ColumnId, c => c.Count);

        var digestColumns = columns
            .Select(c => new AgentChatDigestColumn(c.Name, countByColumn.GetValueOrDefault(c.ColumnId)))
            .ToList();

        // A column can exist in `counts` without appearing in `columns` only if it was created between the
        // two queries; reporting it as an unnamed bucket is still better than silently dropping its cards.
        foreach (var orphan in counts.Where(c => columns.All(col => col.ColumnId != c.ColumnId)))
        {
            digestColumns.Add(new AgentChatDigestColumn("(cột không xác định)", orphan.Count));
        }

        var total = counts.Sum(c => c.Count);

        if (total == 0)
        {
            return new AgentChatDigest(digestColumns, [], 0, effective.MaxDigestChars);
        }

        var candidates = await _db.Tasks
            .AsNoTracking()
            .Where(t => t.BoardId == boardId)
            .OrderBy(t => t.Position)
            .ThenBy(t => t.Id)
            .Take(DigestCandidateLimit)
            .Select(t => new
            {
                t.Title,
                t.ColumnId,
                t.Priority,
                t.DueDate,
                t.AssigneeId,
            })
            .ToListAsync(ct);

        var assigneeNames = await LoadAssigneeNamesAsync(
            candidates.Where(c => c.AssigneeId.HasValue).Select(c => c.AssigneeId!.Value).Distinct().ToList(),
            ct);

        var columnNames = columns.ToDictionary(c => c.ColumnId, c => c.Name);

        var topTasks = candidates
            // Urgent first, then High, … then anything without a priority; within a rank, the nearest
            // deadline wins (a card with no deadline sorts last), and the title breaks the remaining ties so
            // the digest is stable across two identical requests.
            .OrderBy(c => PriorityRank(c.Priority))
            .ThenBy(c => c.DueDate ?? DateTimeOffset.MaxValue)
            .ThenBy(c => c.Title, StringComparer.Ordinal)
            .Take(effective.MaxDigestTasks)
            .Select(c => new AgentChatDigestTask(
                c.Title,
                columnNames.TryGetValue(c.ColumnId, out var name) ? name : string.Empty,
                c.Priority?.ToString(),
                c.DueDate,
                c.AssigneeId is { } assigneeId && assigneeNames.TryGetValue(assigneeId, out var assignee)
                    ? assignee
                    : null))
            .ToList();

        return new AgentChatDigest(digestColumns, topTasks, total, effective.MaxDigestChars);
    }

    /// <summary>
    /// How many cards the digest considers before ranking. Bounded so a board with thousands of cards costs
    /// one page, not the whole table; a card outside the page can only be missing from the "top" list, never
    /// from the per-column counts.
    /// </summary>
    private const int DigestCandidateLimit = 200;

    private static int PriorityRank(TaskPriority? priority) => priority switch
    {
        TaskPriority.Urgent => 0,
        TaskPriority.High => 1,
        TaskPriority.Medium => 2,
        TaskPriority.Low => 3,
        _ => 4,
    };

    private async Task<Dictionary<Guid, string>> LoadAssigneeNamesAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken ct)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        return await _db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? string.Empty, ct);
    }

    private async Task<IReadOnlyList<AiChatContextComment>> LoadCommentsAsync(
        Guid taskId,
        AgentChatEffectiveOptions effective,
        CancellationToken ct)
    {
        if (effective.MaxCommentsInContext <= 0)
        {
            return [];
        }

        var rows = await _db.TaskComments
            .AsNoTracking()
            .Where(c => c.TaskId == taskId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(effective.MaxCommentsInContext)
            .Select(c => new
            {
                Author = c.Author != null ? c.Author.DisplayName : null,
                c.CreatedAt,
                Content = c.Content ?? string.Empty,
            })
            .ToListAsync(ct);

        // Newest-first from the database, oldest-first for the prompt (the Agent's convention).
        rows.Reverse();

        return rows
            .Select(r => new AiChatContextComment(
                r.Author ?? "(không rõ)",
                Cap(r.Content, effective.MaxCommentCharsInContext),
                r.CreatedAt))
            .ToList();
    }

    // =====================================================================
    // Proposal plumbing
    // =====================================================================

    /// <summary>Re-validates a raw proposal against the live board and workspace.</summary>
    private async Task<AgentChatProposal> NormalizeProposalAsync(
        Guid boardId,
        Guid workspaceId,
        string? proposalJson,
        IReadOnlyList<Guid> attachedTaskIds,
        CancellationToken ct)
    {
        var columns = await _db.BoardColumns
            .AsNoTracking()
            .Where(c => c.BoardId == boardId)
            .OrderBy(c => c.Position)
            .Select(c => new AgentChatContextColumn(c.Id, c.Name, c.IsDone))
            .ToListAsync(ct);

        var memberIds = await _db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.MemberType == MemberType.Human)
            .Select(m => m.UserId)
            .ToListAsync(ct);

        var proposal = AgentChatProposalValidator.Normalize(
            proposalJson,
            attachedTaskIds,
            columns,
            memberIds,
            // `DeepSeek:MaxTaskCount` is the existing hard ceiling on one AI proposal (Phase 3); reused
            // rather than duplicated so a single knob still bounds every "create tasks" path.
            _aiOptions.MaxTaskCount,
            out var error);

        if (proposal is null)
        {
            throw new BadRequestException(error ?? "Đề xuất của AI không hợp lệ.");
        }

        // `MaxProposalsPerAnswer` is clamped to exactly 1, so this is a structural guard rather than a
        // tunable: one answer, one decision a Manager can actually judge.
        return proposal;
    }

    /// <summary>
    /// The shape stored as <c>after_snapshot</c>. It is the validated payload <b>as-is</b>, so the appliers
    /// read exactly the fields the human just reviewed — no second transformation to get wrong.
    /// </summary>
    private static object ToSnapshot(AgentChatProposal proposal)
        => new
        {
            proposal.Kind,
            proposal.Summary,
            proposal.Payload.Tasks,
            proposal.Payload.TaskId,
            proposal.Payload.Title,
            proposal.Payload.Description,
            proposal.Payload.Priority,
            proposal.Payload.DueDate,
            proposal.Payload.AssigneeId,
            proposal.Payload.ColumnId,
        };

    /// <summary>
    /// Picks the task a saved answer becomes a comment on: the requested one, which must live on this
    /// board. There is no fallback to "the first task of the board" on purpose — the answer was written
    /// about the attached cards, and quietly posting it somewhere else is worse than asking the user to
    /// attach one.
    /// </summary>
    private async Task<Guid> ResolveTargetTaskAsync(Guid boardId, Guid? requested, CancellationToken ct)
    {
        if (requested is not { } taskId || taskId == Guid.Empty)
        {
            throw new BadRequestException(
                "Hãy gắn ít nhất một thẻ vào cuộc trò chuyện trước khi lưu câu trả lời thành bình luận.");
        }

        var belongs = await _db.Tasks
            .AsNoTracking()
            .AnyAsync(t => t.Id == taskId && t.BoardId == boardId, ct);

        if (!belongs)
        {
            throw new BadRequestException("Thẻ cần lưu bình luận không thuộc bảng này.");
        }

        return taskId;
    }

    /// <summary>Truncates a prompt/storage value, marking a cut with an ellipsis so a reader can tell.</summary>
    private static string Cap(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
