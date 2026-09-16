namespace TeamNexus.Modules.Reporting.Contracts;

/// <summary>
/// Bộ giá trị <c>mode</c> của chuỗi thời gian (Phase 13 §2) — text tự do, cùng tiền lệ
/// <see cref="ReportScopeTypes"/>: serialize giữ nguyên casing canonical, không lowercase.
/// </summary>
public static class ReportSeriesModes
{
    /// <summary>Một bucket = một ngày (khoảng báo cáo ngắn).</summary>
    public const string Date = "date";

    /// <summary>Một bucket = một tuần, mốc là Thứ Hai (khoảng báo cáo dài).</summary>
    public const string Week = "week";

    /// <summary>Số ngày của một bucket theo <paramref name="mode"/> (mode lạ ⇒ 1).</summary>
    public static int BucketDays(string? mode)
        => string.Equals(mode, Week, StringComparison.Ordinal) ? 7 : 1;

    public static bool IsKnown(string? mode)
        => string.Equals(mode, Date, StringComparison.Ordinal)
           || string.Equals(mode, Week, StringComparison.Ordinal);
}

/// <summary>
/// Một ngày trong chuỗi thời gian (Phase 13 §2.3).
/// <para>
/// <see cref="Date"/> là ngày **theo múi giờ người xem** (đã áp <c>tzOffsetMinutes</c>), không phải UTC.
/// </para>
/// <para>
/// <b><see cref="OpenTasks"/> CỐ Ý không trừ <see cref="Completions"/> của cùng ngày</b>: nó là số task
/// còn mở ở **cuối** ngày. Nhờ vậy <c>openTasks − completions</c> chính là đường "lý tưởng" của biểu đồ
/// burndown, và đường đó **không bao giờ âm** dù dữ liệu có bất thường.
/// </para>
/// </summary>
public sealed record ReportDailyProgress(
    DateOnly Date,
    int OpenTasks,
    int Completions,
    int Creations);

/// <summary>
/// Một tuần trong chuỗi thời gian, dùng khi khoảng báo cáo dài (<c>mode = "week"</c>).
/// </summary>
/// <param name="WeekStart">Thứ Hai đầu tuần (ngày theo múi giờ người xem).</param>
/// <param name="OpenAtEnd">Số task còn mở ở cuối tuần.</param>
public sealed record ReportWeeklyProgress(
    DateOnly WeekStart,
    int Completions,
    int Creations,
    int OpenAtEnd);

/// <summary>
/// Chỉ số năng suất suy ra từ chuỗi (Phase 13 §2.3) — luôn có, không phụ thuộc <c>mode</c>.
/// </summary>
/// <param name="AvgCompletionsPerWeek">Trung bình số task hoàn thành mỗi tuần trong cửa sổ (một chữ số thập phân).</param>
/// <param name="CompletedInRange">Tổng số task hoàn thành trong cửa sổ (khớp <c>ReportPerformance.CompletedInRange</c>).</param>
/// <param name="OpenAtEnd">Số task còn mở ở cuối cửa sổ.</param>
public sealed record ReportVelocity(
    double AvgCompletionsPerWeek,
    int CompletedInRange,
    int OpenAtEnd);

/// <summary>
/// Chuỗi thời gian cho biểu đồ Burndown/Velocity (Phase 13 §2).
/// <para>
/// <c>Days</c> và <c>Weeks</c> **loại trừ nhau**: đúng một trong hai có dữ liệu, cái còn lại là mảng rỗng.
/// Luôn đủ bucket (kể cả bucket toàn số 0) nên biểu đồ không bị "nhảy cột".
/// </para>
/// <para>
/// <c>TzOffsetMinutes</c> là giá trị **thực dùng** (đã clamp) — response trả lại để UI hiển thị và để một
/// request sai tham số không âm thầm cho ra số liệu khác mà không dấu vết.
/// </para>
/// </summary>
public sealed record ReportProgressSeries(
    Guid WorkspaceId,
    Guid? BoardId,
    ReportRange Range,
    string Mode,
    IReadOnlyList<ReportDailyProgress> Days,
    IReadOnlyList<ReportWeeklyProgress> Weeks,
    ReportVelocity Velocity,
    int BucketDays,
    bool BucketCapReached,
    int MaxBuckets,
    int TzOffsetMinutes,
    IReadOnlyDictionary<string, string> MetricDefinitions)
{
    /// <summary>
    /// Phạm vi dữ liệu đã tổng hợp, suy ra giống <c>ReportAggregator.BuildScope</c>: đúng 1 board ⇒ theo
    /// board, còn lại ⇒ toàn workspace. Không cần loader truyền vào.
    /// </summary>
    public ReportScope Scope => BoardId is { } boardId
        ? new ReportScope(ReportScopeTypes.Board, boardId, null)
        : new ReportScope(ReportScopeTypes.Workspace, null, null);
}
