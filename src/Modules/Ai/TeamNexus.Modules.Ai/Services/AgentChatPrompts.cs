using System.Text;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Prompt contract for the board-scoped AI Agent Chat (Phase 14.5 §B).
/// <para>
/// Two things separate this from <see cref="AiChatPrompts"/> (the task chat), and both are on purpose:
/// the model may see <b>several</b> attached tasks plus the board's columns, and it is explicitly
/// allowed to <b>propose</b> a write — but never to claim it performed one. The proposal is data; a human
/// turns it into a <c>Pending</c> action.
/// </para>
/// <para>
/// <see cref="Marker"/> doubles as the branch selector for the offline <c>FakeAiProvider</c>, exactly
/// like <c>AiChatPrompts.Marker</c>. The two markers must stay distinct: the fake answers a task-chat
/// prompt and a board-chat prompt differently, and a shared marker would make the offline suite test the
/// wrong branch.
/// </para>
/// </summary>
public static class AgentChatPrompts
{
    /// <summary>
    /// Identifying fragment of the system prompt. The fake provider recognizes it and the real one simply
    /// carries it as text, so the two can never disagree.
    /// </summary>
    public const string Marker = "AI Agent Chat của TeamNexus";

    /// <summary>Fence the model is told to use for the proposal block; the parser looks for the last one.</summary>
    public const string ProposalFence = "```json";

    /// <summary>
    /// System prompt template. <c>{0}</c> is the context block from <see cref="BuildContextBlock"/>.
    /// Every rule that matters is stated here rather than in code comments, because this text <i>is</i>
    /// the contract with the model.
    /// </summary>
    public const string SystemPromptTemplate =
        """
        Bạn là AI Agent Chat của TeamNexus. Bạn trò chuyện với một thành viên trong nhóm về MỘT BẢNG
        KANBAN: nhiều thẻ cùng lúc, cấu trúc cột của bảng, và các thẻ mà người dùng đã kéo vào cuộc trò
        chuyện. Toàn bộ hiểu biết của bạn về dự án nằm trong ngữ cảnh dưới đây.

        NGỮ CẢNH BẢNG:
        {0}

        QUY TẮC BẮT BUỘC:
        1. Chỉ dùng thông tin trong ngữ cảnh trên. TUYỆT ĐỐI không bịa thẻ, người, cột, hạn chót hay số
           liệu không có trong đó. Nếu thiếu thông tin, hãy nói rõ bạn cần thêm gì.
        2. Bạn CHƯA thực hiện bất kỳ thay đổi nào. Không được nói "đã tạo", "đã sửa", "đã chuyển",
           "đã xoá". Bạn chỉ được ĐỀ XUẤT; con người sẽ duyệt (Accountability Layer) trước khi dữ liệu
           thay đổi thật.
        3. Trả lời trực tiếp, ngắn gọn, ưu tiên gạch đầu dòng khi liệt kê.
        4. Giữ nguyên ngôn ngữ tiếng Việt có dấu.
        5. Không dùng HTML; chỉ văn bản thuần (có thể xuống dòng).

        NẾU BẠN MUỐN ĐỀ XUẤT MỘT THAY ĐỔI:
        - Viết phần giải thích bằng tiếng Việt trước, rồi kết thúc câu trả lời bằng ĐÚNG MỘT khối mã
          ```json ... ``` chứa đúng cấu trúc sau và không thêm gì sau khối đó:
        {
          "proposal": {
            "kind": "CreateSubtasks" | "UpdateTaskFields" | "MoveTaskToColumn",
            "summary": "lý do ngắn gọn bằng tiếng Việt, tối đa 300 ký tự",
            "payload": { ... xem bên dưới ... }
          }
        }
        - Chỉ được đề xuất ĐÚNG MỘT thay đổi cho mỗi câu trả lời.
        - Chỉ được thao tác trên các thẻ CÓ TRONG ngữ cảnh (danh sách thẻ đang gắn). Không được nhắc tới
          thẻ khác.
        - "columnId" bắt buộc phải là một id trong danh sách cột ở trên.
        - "CreateSubtasks": payload = { "tasks": [ { "title": "...", "description": "..." | null,
          "columnId": "...", "priority": "Low" | "Medium" | "High" | "Urgent" | null,
          "assigneeId": "..." | null } ] }
        - "UpdateTaskFields": payload = { "taskId": "...", "title": "..." | null,
          "description": "..." | null, "priority": "..." | null, "dueDate": "ISO-8601" | null,
          "assigneeId": "..." | null }
        - "MoveTaskToColumn": payload = { "taskId": "...", "columnId": "..." }
        - Nếu bạn KHÔNG đề xuất thay đổi nào, chỉ cần trả lời bằng văn bản và KHÔNG tạo khối json.
        """;

    /// <summary>The rendered system prompt for one board context.</summary>
    public static string BuildSystemPrompt(string contextBlock)
        => SystemPromptTemplate.Replace("{0}", contextBlock, StringComparison.Ordinal);

    /// <summary>
    /// Human-readable context block fed into <see cref="BuildSystemPrompt"/>. Pure and bounded: the caller
    /// has already truncated every value, so this only shapes text — and therefore needs no database, no
    /// clock and no provider to be verifiable.
    /// </summary>
    public static string BuildContextBlock(AgentChatBoardContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var builder = new StringBuilder();

        builder.AppendLine($"- Bảng: {context.BoardName}");

        if (!string.IsNullOrWhiteSpace(context.BoardDescription))
        {
            builder.AppendLine($"- Mô tả bảng: {context.BoardDescription}");
        }

        if (context.Columns.Count > 0)
        {
            builder.AppendLine("- Cột (theo thứ tự, id — tên — hoàn thành?):");

            foreach (var column in context.Columns)
            {
                builder.AppendLine($"  * {column.ColumnId} — {column.Name} — {(column.IsDone ? "có" : "không")}");
            }
        }

        if (context.AttachedTasks.Count == 0)
        {
            builder.AppendLine("- Thẻ đang gắn: (không có thẻ nào — người dùng chỉ hỏi về bảng)");
        }
        else
        {
            builder.AppendLine($"- Thẻ đang gắn ({context.AttachedTasks.Count}):");

            foreach (var task in context.AttachedTasks)
            {
                builder.AppendLine($"  * [{task.TaskId}] {task.Title}");
                builder.AppendLine($"    Cột: {task.ColumnName} · Ưu tiên: {task.Priority ?? "(chưa đặt)"}");
                builder.AppendLine(
                    task.DueDate is { } due
                        ? $"    Hạn chót: {due:yyyy-MM-dd HH:mm} (UTC)"
                        : "    Hạn chót: (không có)");
                builder.AppendLine(
                    string.IsNullOrWhiteSpace(task.AssigneeName)
                        ? "    Người phụ trách: (chưa gán)"
                        : $"    Người phụ trách: {task.AssigneeName}");

                if (!string.IsNullOrWhiteSpace(task.Description))
                {
                    builder.AppendLine("    Mô tả:");
                    builder.AppendLine(task.Description);
                }

                if (task.Comments.Count > 0)
                {
                    builder.AppendLine($"    {task.Comments.Count} bình luận gần nhất:");

                    foreach (var comment in task.Comments)
                    {
                        builder.AppendLine(
                            $"      - {comment.AuthorName} ({comment.CreatedAt:yyyy-MM-dd HH:mm} UTC): {comment.Content}");
                    }
                }
            }
        }

        if (context.Members.Count > 0)
        {
            builder.AppendLine("- Thành viên có thể được giao việc (id — tên):");

            foreach (var member in context.Members)
            {
                builder.AppendLine($"  * {member.UserId} — {member.DisplayName}");
            }
        }

        return builder.ToString().TrimEnd();
    }
}

/// <summary>
/// Everything the board-chat prompt knows, already truncated and bounded by the caller. A record rather
/// than entities so <see cref="AgentChatPrompts.BuildContextBlock"/> stays pure.
/// </summary>
public sealed record AgentChatBoardContext(
    string BoardName,
    string? BoardDescription,
    IReadOnlyList<AgentChatContextColumn> Columns,
    IReadOnlyList<AgentChatContextTask> AttachedTasks,
    IReadOnlyList<AgentChatContextMember> Members);

/// <summary>One board column as the prompt sees it: the model needs the id to propose a move.</summary>
public sealed record AgentChatContextColumn(Guid ColumnId, string Name, bool IsDone);

/// <summary>One attached task as the prompt sees it (all values already truncated).</summary>
public sealed record AgentChatContextTask(
    Guid TaskId,
    string Title,
    string ColumnName,
    string? Description,
    string? Priority,
    DateTimeOffset? DueDate,
    string? AssigneeName,
    IReadOnlyList<AiChatContextComment> Comments);

/// <summary>
/// A member the model may propose as assignee. AI Agent members are deliberately absent: the executor is
/// driven by assigning a task to the agent through the existing UI, not by a chat proposal.
/// </summary>
public sealed record AgentChatContextMember(Guid UserId, string DisplayName);
