using TeamNexus.Modules.Reporting.Contracts;

namespace TeamNexus.Modules.Reporting.Services;

/// <summary>
/// Snapshot ngưỡng/cap của báo cáo, dựng từ <c>ReportsOptions</c> (Phase 6 §1.4) để
/// <see cref="ReportAggregator"/> không phụ thuộc <c>IOptions</c> — cùng pattern
/// <c>ObserverThresholds</c> của module Ai (Phase 5 §3.2).
/// <para>
/// Mọi giá trị được clamp phòng thủ: config đặt 0/âm (lỗi cấu hình hoặc biến môi trường sai) không thể
/// làm báo cáo rỗng bất khả thi hay gây chia cho 0.
/// </para>
/// </summary>
public sealed record ReportThresholds(
    int MaxExportRows,
    int MaxActionsPerReport,
    int MaxRangeDays,
    int DefaultRangeDays,
    bool ExcludeDoneOverdue,
    int MaxSeriesBuckets,
    int SeriesWeeklyThresholdDays)
{
    /// <summary>Cửa sổ mặc định khi request không truyền <c>from</c>/<c>to</c>.</summary>
    public static ReportThresholds Default { get; } = new(
        MaxExportRows: 5000,
        MaxActionsPerReport: 200,
        MaxRangeDays: 365,
        DefaultRangeDays: 30,
        ExcludeDoneOverdue: true,
        MaxSeriesBuckets: 90,
        SeriesWeeklyThresholdDays: 60);

    /// <summary>
    /// Dựng cửa sổ báo cáo từ tham số request (Phase 6 §1.4) — **hàm thuần**, là nơi DUY NHẤT tính cửa sổ
    /// (cả endpoint §5 và options đều đi qua đây ⇒ một công thức, hai lối vào).
    /// <para>
    /// Quy tắc: <c>to</c> mặc định là <paramref name="now"/> và không bao giờ ở tương lai;
    /// <c>from</c> mặc định là <c>to − DefaultRangeDays</c>; khoảng bị cắt về <see cref="MaxRangeDays"/>
    /// (bật <c>Clamped</c>); <c>Days</c> = ceil(số ngày, min 1).
    /// </para>
    /// <para>
    /// ⚠️ <c>from &gt; to</c> **không** ném ở đây: §1 không có hợp đồng exception (đó là việc của §3 với
    /// <c>InvalidReportRangeException</c> → 400). §3 phải validate trước khi gọi; nếu lọt vào đây thì
    /// cửa sổ được kéo về 1 ngày thay vì làm hỏng báo cáo.
    /// </para>
    /// </summary>
    public ReportRange BuildRange(DateTimeOffset? from, DateTimeOffset? to, DateTimeOffset now)
    {
        var defaultDays = Math.Clamp(DefaultRangeDays, 1, MaxRangeDays);

        var end = to ?? now;
        if (end > now)
        {
            end = now;
        }

        var start = from ?? end.AddDays(-defaultDays);

        var clamped = false;
        if (start > end)
        {
            // Dữ liệu đầu vào ngược thứ tự: kéo về 1 ngày để báo cáo vẫn hợp lệ (validate là việc của §3).
            start = end.AddDays(-1);
        }
        else if ((end - start).TotalDays > MaxRangeDays)
        {
            start = end.AddDays(-MaxRangeDays);
            clamped = true;
        }

        var days = Math.Max(1, (int)Math.Ceiling((end - start).TotalDays));

        return new ReportRange(start, end, days, clamped, BuildLabel(start, end));
    }

    /// <summary>Nhãn hiển thị dùng chung cho PDF/Excel/filename: "dd/MM/yyyy – dd/MM/yyyy (UTC)".</summary>
    public static string BuildLabel(DateTimeOffset from, DateTimeOffset to)
        => $"{from.UtcDateTime:dd/MM/yyyy} – {to.UtcDateTime:dd/MM/yyyy} (UTC)";
}
