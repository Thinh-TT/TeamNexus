using System.Text;
using TeamNexus.Modules.Board.DTOs;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Prompt construction for Smart Setup (Phase 3 §4.3 steps 5–6). Kept separate from the DTOs so
/// the wording (which the JSON-mode contract depends on) is easy to review and tune in one place.
/// <para>
/// Cost control (`02-tech-stack-decisions.md` §5): the context carries only names — board name,
/// column names, member display names and existing label names — never task bodies or history.
/// </para>
/// </summary>
internal static class SmartSetupPrompts
{
    /// <summary>Hard JSON contract the model must satisfy; also restated on retry.</summary>
    private const string SchemaContract = """
        Trả về DUY NHẤT một object JSON (không kèm văn bản, không bọc trong markdown/code fence) đúng dạng:
        {
          "summary": "<tóm tắt ngắn gọn bằng ngôn ngữ của mô tả đầu vào>",
          "tasks": [
            {
              "title": "<tiêu đề sub-task, 1-200 ký tự, bắt buộc>",
              "description": "<mô tả ngắn, tối đa 2000 ký tự, hoặc null>",
              "priority": "Low" | "Medium" | "High" | "Urgent" | null,
              "labels": ["<tối đa 5 nhãn, mỗi nhãn tối đa 80 ký tự>"],
              "suggestedAssignee": "<tên thành viên trong danh sách bên dưới, hoặc null>"
            }
          ]
        }
        """;

    private const string Rules = """
        Bạn là trợ lý lập kế hoạch cho một bảng Kanban của đội phát triển phần mềm.
        Nhiệm vụ: phân rã mô tả công việc của trưởng nhóm thành các sub-task cụ thể, có thể thực thi được.

        Quy tắc bắt buộc:
        - Chỉ trả về JSON, không thêm giải thích, không dùng code fence.
        - Mỗi sub-task phải có "title" rõ ràng, ngắn gọn (1-200 ký tự).
        - "priority" chỉ được là "Low", "Medium", "High", "Urgent" hoặc null.
        - "labels": tối đa 5 nhãn, mỗi nhãn tối đa 80 ký tự; ưu tiên các nhãn đã có trong danh sách nhãn hiện có.
        - "suggestedAssignee": CHỈ được dùng tên có trong danh sách thành viên workspace được cung cấp; nếu không có ai phù hợp thì để null. Tuyệt đối không bịa tên người ngoài danh sách.
        - Giữ nguyên ngôn ngữ của mô tả đầu vào khi viết title/description/summary.
        """;

    public static string BuildSystemPrompt() => Rules + "\n\n" + SchemaContract;

    /// <summary>Context + the team lead's description (step 6).</summary>
    public static string BuildUserPrompt(
        string boardName,
        IReadOnlyList<string> columnNames,
        IReadOnlyList<WorkspaceMemberResponse> members,
        IReadOnlyList<string> labelNames,
        string description)
    {
        var builder = new StringBuilder();

        builder.AppendLine("Bối cảnh board Kanban:");
        builder.AppendLine($"- Board: {boardName}");
        builder.AppendLine($"- Các cột hiện có: {JoinOrPlaceholder(columnNames, "chưa có cột nào")}");
        builder.AppendLine($"- Thành viên workspace (chỉ được chọn trong danh sách này): {BuildMemberList(members)}");
        builder.AppendLine($"- Nhãn đã có trong workspace: {JoinOrPlaceholder(labelNames, "chưa có nhãn nào")}");
        builder.AppendLine();
        builder.AppendLine("Mô tả công việc của trưởng nhóm:");
        builder.AppendLine(description);
        builder.AppendLine();
        builder.Append("Hãy phân rã mô tả trên thành các sub-task và trả về JSON theo đúng schema đã mô tả.");

        return builder.ToString();
    }

    /// <summary>
    /// Retry prompt (step 8): same context, plus the parse failure and a restatement of the schema.
    /// </summary>
    public static string BuildRetryUserPrompt(string originalUserPrompt, string parseError)
        => new StringBuilder()
            .AppendLine(originalUserPrompt)
            .AppendLine()
            .AppendLine("LƯU Ý QUAN TRỌNG: lần trả lời trước đó KHÔNG phải JSON hợp lệ.")
            .AppendLine($"Lỗi khi parse: {parseError}")
            .AppendLine("Hãy trả lại CHỈ một object JSON hợp lệ, không kèm văn bản nào khác, không dùng code fence.")
            .AppendLine()
            .Append(SchemaContract)
            .ToString();

    private static string BuildMemberList(IReadOnlyList<WorkspaceMemberResponse> members)
        => members.Count == 0
            ? "(workspace chưa có thành viên nào — để suggestedAssignee là null)"
            : string.Join(", ", members.Select(m => $"{m.DisplayName} ({m.Role})"));

    private static string JoinOrPlaceholder(IReadOnlyList<string> values, string placeholder)
        => values.Count == 0 ? $"({placeholder})" : string.Join(", ", values);
}
