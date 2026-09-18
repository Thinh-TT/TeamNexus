using System.Text;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Prompt contract for the AI Task Chat (Phase 14 §2.2).
/// <para>
/// The chat is a <b>read/interpret</b> layer for one task: the prompt hands the model exactly one
/// task's context, forbids inventing anything outside it, and forbids proposing write actions — because
/// the only write path is the user explicitly saving the answer through the Accountability Layer.
/// </para>
/// <para>
/// <see cref="Marker"/> doubles as the branch selector for the offline <c>FakeAiProvider</c>, exactly
/// like <c>ObserverPrompts.AgentMarker</c> does for the Observer.
/// </para>
/// </summary>
public static class AiChatPrompts
{
    /// <summary>
    /// Identifying fragment of the system prompt. The fake provider recognizes it and the real one
    /// simply carries it as text; keeping it a constant means the two can never disagree.
    /// </summary>
    public const string Marker = "AI Task Chat của TeamNexus";

    /// <summary>
    /// Marker for the task title inside the context block. The offline provider echoes the title back in
    /// its sample answer, which makes a manual smoke test obviously about the task in front of you.
    /// </summary>
    public const string TaskTitleMarker = "- Tiêu đề";

    /// <summary>
    /// System prompt template. <c>{0}</c> is the context block built by
    /// <see cref="BuildContextBlock"/>; every rule that matters is stated here rather than in code
    /// comments, because this text is the actual contract with the model.
    /// </summary>
    public const string SystemPromptTemplate =
        """
        Bạn là trợ lý AI Task Chat của TeamNexus. Bạn trò chuyện với một thành viên trong nhóm về
        ĐÚNG MỘT task đang mở, dựa trên ngữ cảnh được cung cấp dưới đây.

        NGỮ CẢNH TASK:
        {0}

        QUY TẮC BẮT BUỘC:
        1. Chỉ dùng thông tin trong ngữ cảnh trên. TUYỆT ĐỐI không bịa task, người, hạn chót hay số
           liệu không có trong đó. Nếu thiếu thông tin, hãy nói rõ là bạn cần thêm thông tin.
        2. Đây là kênh HỎI ĐÁP. Không khẳng định rằng bạn đã tạo/sửa/xoá task, không tự nhận đã ghi
           dữ liệu. Mọi thay đổi dữ liệu phải do con người duyệt (Accountability Layer).
        3. Trả lời trực tiếp, ngắn gọn, ưu tiên gạch đầu dòng khi liệt kê. Tối đa 2000 ký tự.
        4. Giữ nguyên ngôn ngữ tiếng Việt có dấu.
        5. Không dùng HTML; chỉ văn bản thuần (có thể xuống dòng).
        """;

    /// <summary>The rendered system prompt for one task context.</summary>
    public static string BuildSystemPrompt(string contextBlock)
        => SystemPromptTemplate.Replace("{0}", contextBlock, StringComparison.Ordinal);

    /// <summary>
    /// Human-readable context block fed into <see cref="BuildSystemPrompt"/>. Pure and bounded: the
    /// caller has already truncated every value, so this only shapes text (and therefore needs no
    /// database or clock to test).
    /// </summary>
    public static string BuildContextBlock(AiChatTaskContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var builder = new StringBuilder();

        builder.AppendLine($"- Tiêu đề: {context.Title}");

        if (!string.IsNullOrWhiteSpace(context.BoardName) || !string.IsNullOrWhiteSpace(context.ColumnName))
        {
            builder.AppendLine($"- Bảng / cột: {context.BoardName} / {context.ColumnName}");
        }

        builder.AppendLine($"- Ưu tiên: {context.Priority ?? "(chưa đặt)"}");

        builder.AppendLine(
            context.DueDate is { } due
                ? $"- Hạn chót: {due:yyyy-MM-dd HH:mm} (UTC)"
                : "- Hạn chót: (không có)");

        builder.AppendLine(
            string.IsNullOrWhiteSpace(context.AssigneeName)
                ? "- Người phụ trách: (chưa gán)"
                : $"- Người phụ trách: {context.AssigneeName}");

        if (!string.IsNullOrWhiteSpace(context.Description))
        {
            builder.AppendLine("- Mô tả:");
            builder.AppendLine(context.Description);
        }

        if (context.Comments.Count > 0)
        {
            builder.AppendLine($"- {context.Comments.Count} bình luận gần nhất:");

            foreach (var comment in context.Comments)
            {
                builder.AppendLine($"  * {comment.AuthorName} ({comment.CreatedAt:yyyy-MM-dd HH:mm} UTC): {comment.Content}");
            }
        }

        return builder.ToString().TrimEnd();
    }
}

/// <summary>
/// Everything the chat prompt knows about one task, already truncated by the caller. A record rather
/// than the entity so <see cref="AiChatPrompts.BuildContextBlock"/> stays a pure function.
/// </summary>
public sealed record AiChatTaskContext(
    Guid TaskId,
    string Title,
    string BoardName,
    string ColumnName,
    string? Description,
    string? Priority,
    DateTimeOffset? DueDate,
    string? AssigneeName,
    IReadOnlyList<AiChatContextComment> Comments);

/// <summary>One task comment as the chat prompt sees it (author display name + already-shortened body).</summary>
public sealed record AiChatContextComment(
    string AuthorName,
    string Content,
    DateTimeOffset CreatedAt);
