namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Prompt contract for the AI Observer (Phase 5 §4.2) — the counterpart of
/// <c>SmartSetupPrompts</c> for the Smart Setup flow.
/// <para>
/// The Observer is a <b>read/interpret</b> layer: the prompt forbids inventing tasks or users,
/// forbids proposing write actions, and requires the answer to be a single JSON object whose
/// evidence ids already came from the summarized signals. Anything the model returns is still
/// re-validated locally (<see cref="ObserverFindingValidator"/>).
/// </para>
/// </summary>
public static class ObserverPrompts
{
    /// <summary>
    /// First line of the user prompt. Doubles as the marker the offline <c>FakeAiProvider</c>
    /// recognizes to answer with a sample findings payload instead of a Smart Setup proposal (§4.3).
    /// </summary>
    public const string AgentMarker = """{"agent":"observer"}""";

    /// <summary>Upper bound on findings requested from the model (kept small to bound cost).</summary>
    public const int MaxFindings = 10;

    public const string SystemPrompt =
        """
        Bạn là AI Observer của TeamNexus — trợ lý giám sát tiến độ cho QUẢN LÝ dự án.
        Bạn nhận một bản TÓM TẮT tín hiệu bất thường đã được hệ thống phát hiện sẵn
        (task quá hạn, task đứng yên, task sắp hết hạn mà không ai cập nhật, người quá tải,
        nghẽn ở cột Kanban) và diễn giải chúng.

        QUY TẮC BẮT BUỘC:
        1. Chỉ được dùng ID (task/user) có trong trường "evidence" của tín hiệu. TUYỆT ĐỐI không
           bịa ID, không tạo task/user mới, không suy đoán thêm dữ liệu ngoài phần tóm tắt.
        2. Không đề xuất hay mô tả hành động ghi dữ liệu (không tạo/sửa/xoá task, không đổi người
           phụ trách). Bạn chỉ cảnh báo và giải thích.
        3. Mỗi finding phải giữ đúng "type" của tín hiệu nguồn, thuộc một trong:
           OverdueTask, StalledTask, AtRiskDeadline, Overload, Bottleneck.
           Riêng AtRiskDeadline nghĩa là: task CHƯA quá hạn nhưng còn rất ít thời gian và đã lâu
           không ai cập nhật — hãy nhấn mạnh cần rà soát NGAY trước khi nó trở thành quá hạn.
        4. "severity" thuộc một trong: Low, Medium, High, Critical. Ưu tiên đúng mức mà tín hiệu
           nguồn đã ghi, chỉ điều chỉnh khi có lý do rõ ràng trong dữ liệu.
        5. Tối đa {0} finding, xếp mức nghiêm trọng giảm dần. Nếu không có gì đáng báo cáo, trả về
           mảng rỗng.
        6. Trả về DUY NHẤT một object JSON, không thêm chữ nào ngoài JSON, không dùng markdown:
           { "findings": [ { "type": "...", "severity": "...", "title": "...", "message": "...",
                             "taskIds": ["..."], "userIds": ["..."] } ] }
        7. "title" ngắn gọn (≤ 200 ký tự), "message" là lời giải thích cho quản lý (≤ 2000 ký tự,
           nêu con số cụ thể và gợi ý rà soát). Giữ nguyên ngôn ngữ tiếng Việt.
        """;

    /// <summary>The rendered system prompt (max findings substituted in).</summary>
    public static string BuildSystemPrompt()
        => SystemPrompt.Replace("{0}", MaxFindings.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
