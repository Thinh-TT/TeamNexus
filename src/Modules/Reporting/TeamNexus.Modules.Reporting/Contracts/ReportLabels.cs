namespace TeamNexus.Modules.Reporting.Contracts;

/// <summary>
/// Nhãn tiếng Việt cho các mã (action / signal type / severity / nhóm chưa gán) khi hiển thị trong báo cáo
/// PDF và Excel (Phase 6 §4 — quyết định D24).
/// <para>
/// Đây là **cùng bộ nhãn** đang dùng ở UI (`frontend/src/features/ai/components/NotificationItem.tsx`:
/// <c>OverdueTask → "Quá hạn"</c>, <c>StalledTask → "Đình trệ"</c>, <c>Overload → "Quá tải"</c>,
/// <c>Bottleneck → "Nghẽn việc"</c>) để 3 bề mặt UI/PDF/Excel không lệch nhau. Giá trị lạ (dữ liệu AI cũ,
/// action tự do) được trả về **nguyên văn** — không nuốt mất thông tin.
/// </para>
/// </summary>
public static class ReportLabels
{
    /// <summary>Nhãn tín hiệu/loại cảnh báo của Observer.</summary>
    public static string SignalType(string? type) => type switch
    {
        "OverdueTask" => "Quá hạn",
        "StalledTask" => "Đình trệ",
        "Overload" => "Quá tải",
        "Bottleneck" => "Nghẽn việc",
        null or "" => ReportSeverities.Unknown,
        _ => type,
    };

    /// <summary>Nhãn mức độ cảnh báo.</summary>
    public static string Severity(string? severity) => ReportSeverities.Normalize(severity) switch
    {
        ReportSeverities.Low => "Thấp",
        ReportSeverities.Medium => "Trung bình",
        ReportSeverities.High => "Cao",
        ReportSeverities.Critical => "Nghiêm trọng",
        _ => "Không xác định",
    };

    /// <summary>Nhãn hành động ghi trong <c>activity_logs</c>.</summary>
    public static string Action(string? action) => action switch
    {
        ReportActionTypes.TaskCreated => "Tạo task",
        ReportActionTypes.TaskUpdated => "Cập nhật task",
        ReportActionTypes.TaskMoved => "Di chuyển task",
        ReportActionTypes.TaskCompleted => "Hoàn thành task",
        ReportActionTypes.TaskDeleted => "Xoá task",
        ReportActionTypes.CommentAdded => "Bình luận",
        null or "" => ReportSeverities.Unknown,
        _ => action,
    };

    /// <summary>
    /// Nhãn kèm mã gốc — dùng cho các khối cần truy vết ngược về dữ liệu gốc
    /// (hoạt động, tín hiệu): <c>"Tạo task (TaskCreated)"</c>; nếu nhãn trùng mã thì chỉ in một lần.
    /// </summary>
    public static string WithCode(string? code, string label)
        => string.IsNullOrEmpty(code) || string.Equals(code, label, StringComparison.Ordinal)
            ? label
            : $"{label} ({code})";
}
