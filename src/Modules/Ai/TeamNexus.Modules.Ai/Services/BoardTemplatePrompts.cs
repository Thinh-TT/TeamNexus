namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Prompt contract for the AI Board Template (Phase 14 §4, decision D12/D13).
/// <para>
/// The model proposes a whole board — its columns and a handful of starter tasks — from one project
/// description. The two hard product rules ("at least two columns, exactly one of them is the done
/// lane" and "5–10 starter tasks") are stated in the prompt <b>and</b> enforced again by
/// <see cref="BoardTemplateValidator"/>, because prompt text is a request, not a guarantee.
/// </para>
/// </summary>
public static class BoardTemplatePrompts
{
    /// <summary>Lower bound on proposed columns (a one-lane board cannot express a workflow).</summary>
    public const int MinColumns = 2;

    /// <summary>Upper bound on proposed columns (more lanes than this stops being a Kanban board).</summary>
    public const int MaxColumns = 6;

    /// <summary>Lower bound on starter tasks, per the roadmap's "5–10 task khởi đầu".</summary>
    public const int MinTasks = 5;

    /// <summary>
    /// Upper bound on starter tasks. The roadmap says 5–10; <c>DeepSeekOptions.MaxTaskCount</c> is still
    /// applied as a second, operator-configurable ceiling on top of this one.
    /// </summary>
    public const int MaxTasks = 10;

    public const int MaxColumnNameLength = 80;

    public const int MaxBoardNameLength = 120;

    public const string Marker = "AI Board Template của TeamNexus";

    /// <summary>System prompt. <c>{0}</c> = <see cref="MaxColumns"/>, <c>{1}</c> = <see cref="MaxTasks"/>.</summary>
    public const string SystemPrompt =
        """
        Bạn là AI Board Template của TeamNexus. Nhiệm vụ của bạn là biến MỘT đoạn mô tả dự án của
        trưởng nhóm thành cấu trúc bảng Kanban hoàn chỉnh để họ duyệt trước khi tạo thật.

        Bạn PHẢI đề xuất:
        1. Tên bảng ngắn gọn, đúng trọng tâm dự án (≤ 120 ký tự).
        2. Từ 2 đến {0} CỘT theo đúng luồng công việc của dự án này, xếp theo thứ tự công việc đi qua.
           Đánh dấu isDone = true cho ĐÚNG MỘT cột — cột mà khi thẻ nằm ở đó thì coi như đã xong.
           Các cột còn lại isDone = false.
        3. Từ 5 đến {1} TASK khởi đầu, mỗi task nêu rõ "columnName" thuộc MỘT trong các cột bạn vừa đề xuất.
           Task phải cụ thể, làm được ngay, không phải danh mục chung chung.

        QUY TẮC BẮT BUỘC:
        - Chỉ dùng tên người/nhãn có trong danh sách được cung cấp. Nếu không chắc, để null.
        - "priority" thuộc một trong: Low, Medium, High, Urgent (hoặc null). "labels" tối đa 5 nhãn.
        - Không bịa id. Không thêm cột "Done" thứ hai.
        - Trả về DUY NHẤT một object JSON, không thêm chữ nào ngoài JSON, không dùng markdown:
          {
            "summary": "...",
            "boardName": "...",
            "boardDescription": "...",
            "columns": [ { "name": "...", "isDone": false } ],
            "tasks": [ { "title": "...", "description": "...", "priority": "High",
                         "columnName": "...", "labels": ["..."], "suggestedAssignee": "..." } ]
          }

        Giữ nguyên ngôn ngữ tiếng Việt có dấu.
        """;

    public static string BuildSystemPrompt()
        => SystemPrompt
            .Replace("{0}", MaxColumns.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{1}", MaxTasks.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>
    /// The team lead's description plus the workspace vocabulary the proposal must stay inside. Bounded
    /// by the caller (description length, member/label counts) so this stays a pure text builder.
    /// </summary>
    public static string BuildUserPrompt(
        string workspaceName,
        IReadOnlyList<string> memberNames,
        IReadOnlyList<string> labelNames,
        string description)
    {
        var members = memberNames.Count == 0 ? "(chưa có)" : string.Join(", ", memberNames);
        var labels = labelNames.Count == 0 ? "(chưa có)" : string.Join(", ", labelNames);

        return $"""
            Workspace: {workspaceName}
            Thành viên: {members}
            Nhãn đã có: {labels}

            Mô tả dự án của trưởng nhóm:
            {description}

            Hãy đề xuất cấu trúc bảng (cột) và các task khởi đầu theo đúng schema JSON đã nêu.
            """;
    }

    /// <summary>
    /// One stricter retry after a malformed answer. Reuses the Phase 3 approach so the two AI JSON
    /// flows fail the same way and are repaired the same way.
    /// </summary>
    public static string BuildRetryUserPrompt(string userPrompt, string parseError)
        => $"""
            {userPrompt}

            LẦN TRƯỚC BẠN TRẢ VỀ JSON KHÔNG HỢP LỆ. Lỗi:
            {parseError}

            Chỉ trả về DUY NHẤT một object JSON đúng schema, không markdown, không giải thích.
            """;
}
