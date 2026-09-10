namespace TeamNexus.Modules.Reporting.Contracts;

/// <summary>
/// Phạm vi báo cáo (Phase 6 §1). Giá trị là text tự do (DB design §4: enum dự kiến mở rộng dùng text
/// tự do + bộ giá trị gợi ý) — serialize giữ nguyên casing canonical, không lowercase
/// (tiền lệ <c>TaskPriority</c>/<c>ObserverRunStatus</c>).
/// </summary>
public static class ReportScopeTypes
{
    /// <summary>Báo cáo toàn workspace (mọi board).</summary>
    public const string Workspace = "workspace";

    /// <summary>Báo cáo thu hẹp vào một board (<c>?boardId=</c>).</summary>
    public const string Board = "board";
}

/// <summary>
/// Bộ severity dùng cho khối "sức khoẻ dự án" (Phase 6 §1.3).
/// <para>
/// Cố ý COPY giá trị từ hằng số của module Ai thay vì tham chiếu project (Phase 6 §0 D4): module
/// Reporting không được phụ thuộc module Ai, và đây là cùng một bộ giá trị đã lưu trong
/// <c>ai_observer_runs.summary.findings[].severity</c>.
/// </para>
/// </summary>
public static class ReportSeverities
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";
    public const string Critical = "Critical";

    /// <summary>Gom các giá trị severity không nhận diện được (dữ liệu AI cũ/lạ).</summary>
    public const string Unknown = "Unknown";

    /// <summary>Severity đã biết (dùng để phân loại khi tổng hợp <c>summary.findings[]</c>).</summary>
    public static bool IsKnown(string? severity) => severity switch
    {
        Low or Medium or High or Critical => true,
        _ => false,
    };

    /// <summary>Chuẩn hoá giá trị severity: không nhận diện được ⇒ <see cref="Unknown"/>.</summary>
    public static string Normalize(string? severity)
        => IsKnown(severity) ? severity! : Unknown;
}

/// <summary>
/// Bộ giá trị <c>activity_logs.action</c> mà báo cáo nhận diện (Phase 6 §1.3).
/// <para>
/// Copy giá trị khớp <c>ObserverActivityActions</c> của module Board (cùng lý do D4: không tạo
/// project reference sang module Ai). Giá trị lạ vẫn được tổng hợp nguyên văn — bảng dưới chỉ là
/// bộ gợi ý để UI/PDF có thể gắn nhãn tiếng Việt.
/// </para>
/// </summary>
public static class ReportActionTypes
{
    public const string TaskCreated = "TaskCreated";
    public const string TaskUpdated = "TaskUpdated";
    public const string TaskMoved = "TaskMoved";
    public const string TaskCompleted = "TaskCompleted";
    public const string TaskDeleted = "TaskDeleted";
    public const string CommentAdded = "CommentAdded";
}

/// <summary>
/// Domain model của báo cáo (Phase 6 §1 + §4.1) — **nguồn số liệu duy nhất** cho response JSON
/// (<c>GET .../reports/summary</c>) và cho cả hai renderer (PDF/Excel), nên 3 bề mặt không thể lệch nhau.
/// <para>
/// Kiểu ở đây là kiểu domain thuần: không <c>JsonElement</c>, không attribute của ASP.NET, không phụ
/// thuộc EF Core. DTO HTTP (§5.1) là type riêng trong <c>DTOs/</c> và map sang bằng <c>.From(...)</c>.
/// </para>
/// </summary>
public sealed record ReportSummary(
    Guid WorkspaceId,
    string WorkspaceName,
    ReportScope Scope,
    ReportRange Period,
    DateTimeOffset GeneratedAt,
    ReportProgress Progress,
    ReportPerformance Performance,
    IReadOnlyList<ReportBoardRow> ByBoard,
    IReadOnlyList<ReportAssigneeRow> ByAssignee,
    ReportActivity Activity,
    ReportHealth Health,
    IReadOnlyDictionary<string, string> MetricDefinitions,
    ReportTruncation Truncated);

/// <summary>
/// Khoảng thời gian báo cáo (Phase 6 §1.4). <see cref="Days"/> đã được làm tròn lên và tối thiểu 1,
/// <see cref="Clamped"/> = khoảng yêu cầu đã bị cắt về <c>Reports:MaxRangeDays</c>.
/// </summary>
/// <param name="From">Bắt đầu (UTC).</param>
/// <param name="To">Kết thúc (UTC), không bao giờ ở tương lai.</param>
/// <param name="Days">Số ngày của cửa sổ (ceil, min 1) — mẫu số của throughput/actionsPerDay.</param>
/// <param name="Clamped">True khi khoảng yêu cầu vượt <c>MaxRangeDays</c> và đã bị cắt.</param>
/// <param name="Label">Nhãn hiển thị sẵn ("dd/MM/yyyy – dd/MM/yyyy (UTC)") cho PDF/Excel/filename.</param>
public sealed record ReportRange(
    DateTimeOffset From,
    DateTimeOffset To,
    int Days,
    bool Clamped,
    string Label);

/// <summary>
/// Phạm vi dữ liệu đã tổng hợp. Suy ra từ số board trong snapshot (1 board ⇒ <c>board</c>), vì loader
/// (§3) đã lọc snapshot theo scope — aggregator không lọc lại board.
/// </summary>
public sealed record ReportScope(string Type, Guid? BoardId, string? BoardName);

/// <summary>
/// Tiến độ **hiện tại** (không phụ thuộc cửa sổ thời gian). Xem công thức §1.3 của file task.
/// </summary>
public sealed record ReportProgress(int Total, int Done, int Open, int Overdue, double DonePercent);

/// <summary>
/// Hiệu suất **trong cửa sổ** <see cref="ReportRange"/>. Các <c>double?</c> trả <c>null</c> khi mẫu
/// số bằng 0 (UI hiển thị "—") — khác hẳn 0.0 để không nói sai "trung bình 0 giờ".
/// </summary>
public sealed record ReportPerformance(
    int CreatedInRange,
    int CompletedInRange,
    double? AvgCompletionHours,
    double? AvgLeadTimeHours,
    double? OnTimeRate,
    double OverdueRate,
    double ThroughputPerWeek,
    int CompletedAtMissing);

/// <summary>Một dòng trong bảng "Theo bảng" (mọi board đều có dòng, kể cả board 0 task).</summary>
public sealed record ReportBoardRow(
    Guid BoardId,
    string BoardName,
    int Total,
    int Done,
    int Open,
    int Overdue,
    int CompletedInRange,
    double? AvgCompletionHours,
    double? OnTimeRate);

/// <summary>
/// Một dòng trong bảng "Theo người". <see cref="AssigneeId"/> = <c>null</c> là nhóm "Chưa gán"
/// (<c>ReportUnassigned.Name</c>) — không phải lỗi dữ liệu.
/// </summary>
public sealed record ReportAssigneeRow(
    Guid? AssigneeId,
    string AssigneeName,
    int Total,
    int Done,
    int Open,
    int Overdue,
    int CompletedInRange,
    double? AvgCompletionHours,
    double? OnTimeRate);

/// <summary>Số sự kiện theo một giá trị <c>activity_logs.action</c> (giá trị lạ vẫn giữ nguyên văn).</summary>
public sealed record ReportActionRow(string Action, int Count);

/// <summary>Khối hoạt động trong cửa sổ (nguồn: <c>activity_logs</c>).</summary>
public sealed record ReportActivity(
    int TotalActions,
    IReadOnlyList<ReportActionRow> ByAction,
    int ActiveUsers,
    double ActionsPerDay);

/// <summary>Số tín hiệu theo loại (nguồn: <c>ai_observer_runs.summary.signalsByType</c>).</summary>
public sealed record ReportSignalTypeRow(string Type, int Count);

/// <summary>Số finding theo mức độ (nguồn: <c>ai_observer_runs.summary.findings[].severity</c>).</summary>
public sealed record ReportSeverityRow(string Severity, int Count);

/// <summary>
/// Khối "sức khoẻ dự án" tổng hợp từ các run Observer <c>Completed</c> trong cửa sổ. Rỗng (2 mảng rỗng,
/// <see cref="RunsScanned"/> = 0) là hợp lệ — việc có hiển thị hay không do renderer quyết định qua
/// <c>Reports:IncludeHealthSection</c>.
/// </summary>
public sealed record ReportHealth(
    int RunsScanned,
    IReadOnlyList<ReportSignalTypeRow> SignalsByType,
    IReadOnlyList<ReportSeverityRow> FindingsBySeverity);

/// <summary>
/// Cờ cho biết dữ liệu chi tiết đã bị cắt bởi cap (Phase 6 §0 D13). Chỉ có **một** cờ cho toàn báo cáo
/// (không có cờ riêng cho từng mảng) để hợp đồng HTTP đơn giản.
/// </summary>
public sealed record ReportTruncation(bool RowCapReached, int MaxRows);

/// <summary>Tên hiển thị của nhóm task không có người phụ trách.</summary>
public static class ReportUnassigned
{
    public const string Name = "Chưa gán";
}
