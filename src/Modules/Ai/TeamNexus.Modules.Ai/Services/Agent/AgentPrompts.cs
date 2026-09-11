using System.Text;
using TeamNexus.Modules.Ai.Options;

namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>One task comment as it enters the prompt (already trimmed by the caller).</summary>
public sealed record AgentContextComment(string Author, DateTimeOffset CreatedAt, string Content);

/// <summary>Everything the agent is told about the task it is executing (Phase 7 §4.8a).</summary>
public sealed record AgentPromptContext(
    Guid TaskId,
    string Title,
    string? Description,
    string ColumnName,
    string? AssigneeName,
    string? Priority,
    DateTimeOffset? DueDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AgentContextComment> Comments,
    string? PreviousClarificationQuestion = null,
    string? ResolutionComment = null);

/// <summary>
/// Prompt assembly for the AI Agent Executor (Phase 7 §4.8a, `01-system-specification.md` §7).
/// <para>
/// Two hard rules live in the system prompt: the run MUST end through exactly one of
/// <c>DraftOutput</c>/<c>RequestClarification</c>, and the agent must ask instead of inventing
/// missing information. The context builder is the token-cost boundary: bounded comment count,
/// bounded per-comment length and a bounded total.
/// </para>
/// </summary>
public static class AgentPrompts
{
    private const string Rules = """
        Bạn là AI Agent thực thi của một workspace trong TeamNexus. Bạn được giao MỘT task cụ thể và
        phải hoàn thành nó bằng các công cụ được cung cấp.

        Quy tắc bắt buộc:
        - Kết thúc lượt chạy bằng ĐÚNG MỘT trong hai công cụ: `DraftOutput` (đã có kết quả) hoặc
          `RequestClarification` (thiếu thông tin, cần trưởng nhóm trả lời).
        - KHÔNG bịa dữ liệu. Chỉ dùng dữ liệu lấy được từ `SearchSystemData` và `WebSearch`.
        - Nếu thiếu thông tin để làm đúng yêu cầu, hãy hỏi lại bằng `RequestClarification` thay vì
          suy đoán.
        - Bạn KHÔNG tự tạo task/board, KHÔNG tự đổi người thực hiện và KHÔNG tự ghi dữ liệu: kết quả
          của bạn luôn được con người duyệt trước khi trở thành bình luận/tệp đính kèm thật.
        - Dùng `SearchSystemData` khi cần dữ liệu nội bộ của workspace; `WebSearch` chỉ để tham khảo
          bên ngoài và chỉ khi thật sự cần.
        - Trả lời bằng tiếng Việt, ngắn gọn, đi thẳng vào công việc.
        - Kết quả ngắn (vài đoạn) ⇒ dùng `DraftOutput` chỉ với `content`. Kết quả dài hoặc có định
          dạng file ⇒ thêm `fileName`/`contentType` để hệ thống lưu thành tệp đính kèm.
        """;

    /// <summary>
    /// System prompt. The first line is <see cref="AgentMarkers.Executor"/> so the offline provider
    /// can recognise this flow without inspecting the (long) context (Phase 7 §4.5).
    /// </summary>
    public static string BuildSystemPrompt() => AgentMarkers.Executor + "\n" + Rules;

    /// <summary>
    /// The single user message: task metadata, the trailing comments and (on a re-run) the previous
    /// question plus the team lead's answer. Comments are added newest-first until the
    /// <c>Agent:MaxTaskContextChars</c> budget is spent, then re-ordered chronologically so the model
    /// reads them as a conversation.
    /// </summary>
    public static string BuildUserPrompt(AgentPromptContext context, AgentEffectiveOptions options)
    {
        var builder = new StringBuilder();

        builder.AppendLine("Task đang thực thi:");
        builder.AppendLine($"- ID: {context.TaskId}");
        builder.AppendLine($"- Tiêu đề: {context.Title}");
        builder.AppendLine($"- Cột hiện tại: {context.ColumnName}");
        builder.AppendLine($"- Người thực hiện: {context.AssigneeName ?? "(chưa gán)"}");
        builder.AppendLine($"- Ưu tiên: {context.Priority ?? "(không đặt)"}");
        builder.AppendLine($"- Hạn: {(context.DueDate is { } due ? due.ToString("u") : "(không đặt)")}");
        builder.AppendLine($"- Cập nhật gần nhất: {context.UpdatedAt:u}");

        if (!string.IsNullOrWhiteSpace(context.Description))
        {
            builder.AppendLine();
            builder.AppendLine("Mô tả:");
            builder.AppendLine(context.Description!.Trim());
        }

        if (context.PreviousClarificationQuestion is { } previousQuestion)
        {
            builder.AppendLine();
            builder.AppendLine("Câu hỏi bạn đã hỏi ở lượt trước:");
            builder.AppendLine(previousQuestion);
            builder.AppendLine();
            builder.AppendLine("Trưởng nhóm đã trả lời (câu hỏi này ĐÃ được trả lời, đừng hỏi lại):");
            builder.AppendLine(context.ResolutionComment ?? "(không có nội dung)");
        }

        var budget = options.MaxTaskContextChars - builder.Length;
        var selected = SelectComments(context.Comments, options, Math.Max(0, budget));

        if (selected.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"Bình luận gần nhất của task ({selected.Count} bình luận, cũ → mới):");
            foreach (var comment in selected)
            {
                builder.AppendLine($"- [{comment.CreatedAt:u}] {comment.Author}: {comment.Content}");
            }
        }

        builder.AppendLine();
        builder.Append("Hãy thực hiện task trên bằng các công cụ đã cung cấp, và kết thúc bằng ");
        builder.Append("`DraftOutput` hoặc `RequestClarification`.");

        return builder.ToString();
    }

    /// <summary>
    /// Newest-first selection under both the count cap and the remaining character budget, returned
    /// oldest-first. Pure and deterministic, so the token boundary is verifiable without a model.
    /// </summary>
    public static IReadOnlyList<AgentContextComment> SelectComments(
        IReadOnlyList<AgentContextComment> comments, AgentEffectiveOptions options, int budget)
    {
        if (comments.Count == 0 || options.MaxCommentsInContext == 0 || budget <= 0)
        {
            return [];
        }

        var selected = new List<AgentContextComment>();
        var used = 0;

        foreach (var comment in comments.Skip(Math.Max(0, comments.Count - options.MaxCommentsInContext)))
        {
            var content = comment.Content.Length <= options.MaxCommentCharsInContext
                ? comment.Content
                : comment.Content[..options.MaxCommentCharsInContext] + "…";

            var cost = content.Length + comment.Author.Length + 32;
            if (used + cost > budget)
            {
                break;
            }

            used += cost;
            selected.Add(comment with { Content = content });
        }

        return selected;
    }
}
