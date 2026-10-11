using System.Text.Json;
using System.Text.Json.Serialization;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Board.Services;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// The write actions the board chat may propose (Phase 14.5 §B). Kept as a closed set: an unknown
/// <c>kind</c> is a 400, not a new feature discovered at runtime.
/// </summary>
public static class AgentChatProposalKinds
{
    /// <summary>Create one or more new tasks in a column — reuses the Phase 3/4 sub-task applier.</summary>
    public const string CreateSubtasks = "CreateSubtasks";

    /// <summary>Change fields of one attached task.</summary>
    public const string UpdateTaskFields = "UpdateTaskFields";

    /// <summary>Move one attached task to another column of the same board.</summary>
    public const string MoveTaskToColumn = "MoveTaskToColumn";

    /// <summary>Every kind the validator accepts, in the order the prompt lists them.</summary>
    public static readonly IReadOnlyList<string> All =
        [CreateSubtasks, UpdateTaskFields, MoveTaskToColumn];
}

/// <summary>
/// What the model wrote, before validation. Every member is optional and nullable because the raw answer
/// is untrusted: a missing <c>kind</c>, a string where an id belongs, or a <c>payload</c> that is not an
/// object must all be rejected rather than crash the deserializer.
/// </summary>
public sealed class AgentChatProposalOutput
{
    public string? Kind { get; set; }

    public string? Summary { get; set; }

    public AgentChatProposalPayloadOutput? Payload { get; set; }
}

/// <summary>
/// The union of every field any kind may use. One flat shape rather than a polymorphic hierarchy: the
/// validator already branches on <c>kind</c>, and a flat record keeps the JSON contract reviewable in one
/// place — which is exactly what a human approving the action needs to read.
/// </summary>
public sealed class AgentChatProposalPayloadOutput
{
    // ---- CreateSubtasks ----
    public IReadOnlyList<AiProposalTaskOutput>? Tasks { get; set; }

    // ---- UpdateTaskFields / MoveTaskToColumn ----
    public Guid? TaskId { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? Priority { get; set; }

    public DateTimeOffset? DueDate { get; set; }

    public Guid? AssigneeId { get; set; }

    // ---- CreateSubtasks / MoveTaskToColumn ----
    public Guid? ColumnId { get; set; }
}

/// <summary>One task inside a <c>CreateSubtasks</c> proposal: the shape <c>CreateSubtasksApplier</c> expects.</summary>
public sealed class AiProposalTaskOutput
{
    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? Priority { get; set; }

    public Guid? ColumnId { get; set; }

    public Guid? AssigneeId { get; set; }
}

/// <summary>A validated proposal, ready to be shown to a human and then stored as an <c>after_snapshot</c>.</summary>
public sealed record AgentChatProposal(
    string Kind,
    string Summary,
    AgentChatProposalPayload Payload);

/// <summary>
/// The validated payload. Deliberately the <b>same field names</b> the appliers read from
/// <c>after_snapshot</c>, so what the UI previews and what gets applied cannot drift.
/// </summary>
public sealed record AgentChatProposalPayload(
    IReadOnlyList<SmartSetupTaskProposal>? Tasks,
    Guid? TaskId,
    string? Title,
    string? Description,
    string? Priority,
    DateTimeOffset? DueDate,
    Guid? AssigneeId,
    Guid? ColumnId);

/// <summary>
/// Validates and normalizes the proposal the model produced (Phase 14.5 §B).
/// <para>
/// <b>This is the security boundary, not a formatter.</b> The model is untrusted input: it may name a
/// task from another board, invent a column id, emit two proposals, or pass a numeric priority that
/// <c>Enum.TryParse</c> would silently swallow (the Phase 10 BUG-1 trap). Everything here exists to turn
/// that into either a clean proposal or a 400 in Vietnamese that says what was wrong.
/// </para>
/// <para>
/// <b>Pure.</b> No database, no clock, no provider: callers pass the attached task ids, the board's
/// columns and the workspace's member ids that they already loaded. That is what makes the rules testable
/// in isolation — and they are the rules most likely to be wrong.
/// </para>
/// </summary>
public static class AgentChatProposalValidator
{
    /// <summary>Longest <c>summary</c> kept; a longer one is truncated rather than rejected.</summary>
    public const int MaxSummaryLength = 300;

    /// <summary>Longest proposed task title (matches <c>SmartSetupService.MaxTitleLength</c>).</summary>
    public const int MaxTitleLength = 200;

    /// <summary>Longest proposed description.</summary>
    public const int MaxDescriptionLength = 4000;

    /// <summary>Deserialization settings matching the rest of the module (camelCase, case-insensitive).</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The normalized proposal, or <c>null</c> with a Vietnamese reason in <paramref name="error"/>.</summary>
    /// <param name="json">
    /// The JSON body of the fenced block, exactly as parsed from the answer — i.e. still <b>untrusted</b>.
    /// Either shape is accepted: the documented envelope (<c>{ "proposal": { … } }</c>) or a bare proposal.
    /// Accepting both here rather than requiring the caller to unwrap first removes a "must call the right
    /// thing in the right order" contract from the one function whose job is to distrust its input.
    /// </param>
    /// <param name="attachedTaskIds">
    /// Tasks the user attached to <b>this</b> question. The only tasks any proposal may name.
    /// </param>
    /// <param name="columns">Columns of the board, so a proposed <c>columnId</c> can be checked.</param>
    /// <param name="memberIds">Workspace member ids a proposal may assign to.</param>
    /// <param name="maxTasks">Hard ceiling on created tasks (<c>DeepSeek:MaxTaskCount</c>).</param>
    public static AgentChatProposal? Normalize(
        string? json,
        IReadOnlyCollection<Guid> attachedTaskIds,
        IReadOnlyList<AgentChatContextColumn> columns,
        IReadOnlyCollection<Guid> memberIds,
        int maxTasks,
        out string? error)
    {
        error = null;

        // Envelope or bare object — unwrapped once, right at the boundary.
        json = AgentChatProposalParser.UnwrapEnvelope(json ?? string.Empty);

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "AI không trả về đề xuất nào để gửi duyệt.";
            return null;
        }

        AgentChatProposalOutput? output;
        try
        {
            output = JsonSerializer.Deserialize<AgentChatProposalOutput>(json, Json);
        }
        catch (JsonException)
        {
            error = "Nội dung đề xuất của AI không phải JSON hợp lệ — không thể gửi duyệt.";
            return null;
        }

        if (output is null)
        {
            error = "Nội dung đề xuất của AI rỗng — không thể gửi duyệt.";
            return null;
        }

        var kind = output.Kind?.Trim();

        if (string.IsNullOrEmpty(kind))
        {
            error = "Đề xuất của AI thiếu loại hành động (kind).";
            return null;
        }

        // Case-insensitive match, but stored canonically: the appliers switch on the exact string.
        var canonicalKind = AgentChatProposalKinds.All.FirstOrDefault(
            k => string.Equals(k, kind, StringComparison.OrdinalIgnoreCase));

        if (canonicalKind is null)
        {
            error = $"Đề xuất của AI dùng loại hành động không được hỗ trợ: '{kind}'.";
            return null;
        }

        var summary = Cap(output.Summary, MaxSummaryLength);
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = $"Đề xuất {canonicalKind} cho bảng này.";
        }

        var payload = output.Payload;
        if (payload is null)
        {
            error = "Đề xuất của AI thiếu phần nội dung (payload).";
            return null;
        }

        return canonicalKind switch
        {
            AgentChatProposalKinds.CreateSubtasks =>
                NormalizeCreateSubtasks(summary, payload, columns, memberIds, maxTasks, out error),
            AgentChatProposalKinds.UpdateTaskFields =>
                NormalizeUpdateTask(summary, payload, attachedTaskIds, memberIds, out error),
            AgentChatProposalKinds.MoveTaskToColumn =>
                NormalizeMoveTask(summary, payload, attachedTaskIds, columns, out error),
            _ => null,
        };
    }

    // ---- one normalizer per kind -------------------------------------------

    private static AgentChatProposal? NormalizeCreateSubtasks(
        string summary,
        AgentChatProposalPayloadOutput payload,
        IReadOnlyList<AgentChatContextColumn> columns,
        IReadOnlyCollection<Guid> memberIds,
        int maxTasks,
        out string? error)
    {
        error = null;

        if (payload.Tasks is null || payload.Tasks.Count == 0)
        {
            error = "Đề xuất tạo thẻ mới nhưng không có thẻ nào.";
            return null;
        }

        if (maxTasks <= 0)
        {
            error = "Hệ thống đang giới hạn 0 thẻ cho một đề xuất.";
            return null;
        }

        // A done column is a legitimate target (a task can be created already finished), so unlike the
        // move action there is no column-kind restriction here.
        var columnId = ResolveColumn(payload.ColumnId, columns, out error);
        if (columnId is null)
        {
            return null;
        }

        var tasks = new List<SmartSetupTaskProposal>();

        foreach (var raw in payload.Tasks.Take(maxTasks))
        {
            var title = Cap(raw.Title, MaxTitleLength);
            if (string.IsNullOrWhiteSpace(title))
            {
                // Silently dropping an unusable task (rather than failing the whole proposal) matches
                // SmartSetupService.NormalizeTasks: the user is reviewing the list anyway.
                continue;
            }

            var assigneeId = ResolveAssignee(raw.AssigneeId, memberIds);

            tasks.Add(new SmartSetupTaskProposal(
                title,
                Cap(raw.Description, MaxDescriptionLength),
                SmartSetupService.NormalizePriority(raw.Priority),
                // Labels are intentionally NOT proposed by the board chat: the prompt asks for tasks, and
                // an empty list means "no labels" rather than "labels the model guessed".
                [],
                // `Matched: true` because the id was just checked against the member set; the display name
                // stays null — the UI already has the member list and does not need the model's spelling.
                assigneeId is null
                    ? null
                    : new SmartSetupAssigneeSuggestion(assigneeId.Value, null, Matched: true)));
        }

        if (tasks.Count == 0)
        {
            error = "Đề xuất tạo thẻ mới không có thẻ nào hợp lệ (thiếu tiêu đề).";
            return null;
        }

        return new AgentChatProposal(
            AgentChatProposalKinds.CreateSubtasks,
            summary,
            new AgentChatProposalPayload(
                tasks, null, null, null, null, null, null, columnId));
    }

    private static AgentChatProposal? NormalizeUpdateTask(
        string summary,
        AgentChatProposalPayloadOutput payload,
        IReadOnlyCollection<Guid> attachedTaskIds,
        IReadOnlyCollection<Guid> memberIds,
        out string? error)
    {
        error = null;

        var taskId = RequireAttachedTask(payload.TaskId, attachedTaskIds, out error);
        if (taskId is null)
        {
            return null;
        }

        var title = Cap(payload.Title, MaxTitleLength);
        var description = Cap(payload.Description, MaxDescriptionLength);

        // A priority that is present but unrecognised is a 400, NOT a silent null: "the model said Urgent
        // and the system quietly ignored it" is exactly the Phase 10 BUG-1 failure. An absent priority
        // (null/blank) is a legitimate "no change".
        string? priority = null;
        if (!string.IsNullOrWhiteSpace(payload.Priority))
        {
            priority = SmartSetupService.NormalizePriority(payload.Priority);
            if (priority is null)
            {
                error = $"Đề xuất dùng mức ưu tiên không hợp lệ: '{payload.Priority}'.";
                return null;
            }
        }

        if (title is null && description is null && priority is null
            && payload.DueDate is null && payload.AssigneeId is null)
        {
            error = "Đề xuất cập nhật thẻ nhưng không thay đổi trường nào.";
            return null;
        }

        var assigneeId = ResolveAssignee(payload.AssigneeId, memberIds);

        return new AgentChatProposal(
            AgentChatProposalKinds.UpdateTaskFields,
            summary,
            new AgentChatProposalPayload(
                null, taskId, title, description, priority, ToUtc(payload.DueDate), assigneeId, null));
    }

    private static AgentChatProposal? NormalizeMoveTask(
        string summary,
        AgentChatProposalPayloadOutput payload,
        IReadOnlyCollection<Guid> attachedTaskIds,
        IReadOnlyList<AgentChatContextColumn> columns,
        out string? error)
    {
        error = null;

        var taskId = RequireAttachedTask(payload.TaskId, attachedTaskIds, out error);
        if (taskId is null)
        {
            return null;
        }

        var columnId = ResolveColumn(payload.ColumnId, columns, out error);
        if (columnId is null)
        {
            return null;
        }

        return new AgentChatProposal(
            AgentChatProposalKinds.MoveTaskToColumn,
            summary,
            new AgentChatProposalPayload(
                null, taskId, null, null, null, null, null, columnId));
    }

    // ---- shared checks -----------------------------------------------------

    /// <summary>
    /// The task must be one the user attached to <b>this</b> question. This is the single most important
    /// check in the class: without it a model (or a hand-crafted request) could name any task in the
    /// database and have a Manager approve a change to a board they were not even looking at.
    /// </summary>
    private static Guid? RequireAttachedTask(
        Guid? taskId,
        IReadOnlyCollection<Guid> attachedTaskIds,
        out string? error)
    {
        error = null;

        if (taskId is null || taskId == Guid.Empty)
        {
            error = "Đề xuất thiếu thẻ cần thao tác (taskId).";
            return null;
        }

        if (!attachedTaskIds.Contains(taskId.Value))
        {
            error = "Đề xuất thao tác trên một thẻ không được gắn vào cuộc trò chuyện này.";
            return null;
        }

        return taskId;
    }

    /// <summary>
    /// The column must belong to this board, and must not be the executor's "Chờ làm rõ" column: that
    /// column is system state owned by the Agent Executor (Phase 7 D3), and letting chat move tasks into
    /// (or out of) it would corrupt a running clarification cycle.
    /// </summary>
    private static Guid? ResolveColumn(
        Guid? columnId,
        IReadOnlyList<AgentChatContextColumn> columns,
        out string? error)
    {
        error = null;

        if (columnId is null || columnId == Guid.Empty)
        {
            error = "Đề xuất thiếu cột cần thao tác (columnId).";
            return null;
        }

        if (!columns.Any(c => c.ColumnId == columnId.Value))
        {
            error = "Đề xuất dùng một cột không thuộc bảng này.";
            return null;
        }

        return columnId;
    }

    /// <summary>
    /// An assignee id must be a real workspace member. Unlike Smart Setup (which resolves a <i>name</i>),
    /// the board chat is handed ids by the board context, so an id that is not a member is a stale or
    /// invented value — dropped to null with no warning, because the human is about to review the
    /// proposal anyway and an unassigned task is a safe outcome.
    /// </summary>
    private static Guid? ResolveAssignee(Guid? assigneeId, IReadOnlyCollection<Guid> memberIds)
        => assigneeId is null || assigneeId == Guid.Empty || !memberIds.Contains(assigneeId.Value)
            ? null
            : assigneeId;

    /// <summary>
    /// Normalizes a timestamp to UTC. Npgsql throws <c>only offset 0 (UTC) is supported</c> for a
    /// non-zero offset coming out of <c>SaveChangesAsync</c> — the Phase 10 BUG-2 trap — and the value
    /// here originates from a model, so it is the likeliest place in the codebase to see <c>+07:00</c>.
    /// </summary>
    private static DateTimeOffset? ToUtc(DateTimeOffset? value)
        => value?.ToUniversalTime();

    /// <summary>Trims and truncates; blank becomes null so "absent" and "empty" are not two states.</summary>
    private static string? Cap(string? value, int maxLength)
    {
        var text = value?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length <= maxLength ? text : text[..maxLength];
    }
}
