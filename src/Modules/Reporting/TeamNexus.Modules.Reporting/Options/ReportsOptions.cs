using TeamNexus.Modules.Reporting.Contracts;
using TeamNexus.Modules.Reporting.Services;

namespace TeamNexus.Modules.Reporting.Options;

/// <summary>
/// Cấu hình module Reporting (section <c>"Reports"</c> trong appsettings / biến môi trường), theo đúng
/// pattern <c>DeepSeekOptions</c>/<c>ObserverOptions</c> của Phase 3–5: POCO bind từ configuration, không
/// validate cứng lúc khởi động — thiếu section thì dùng đúng default đã tài liệu hoá.
/// <para>
/// Phase 6 §0 D18: mọi ngưỡng/cap nằm ở đây, **không** hard-code, để tinh chỉnh/demo không cần build lại;
/// <see cref="Enabled"/> là công tắc an toàn cho production (tắt ⇒ endpoint trả 503).
/// </para>
/// </summary>
public sealed class ReportsOptions
{
    public const string SectionName = "Reports";

    // ---- công tắc & cửa sổ thời gian ---------------------------------------

    /// <summary>Master switch. <c>false</c> ⇒ mọi endpoint báo cáo trả 503 (không phải lỗi quyền).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Số ngày mặc định khi request không truyền <c>from</c>/<c>to</c>. Clamp 1..<see cref="MaxRangeDays"/>.</summary>
    public int DefaultRangeDays { get; set; } = 30;

    /// <summary>Cửa sổ tối đa (ngày); khoảng dài hơn bị cắt và đánh cờ <c>clamped</c>.</summary>
    public int MaxRangeDays { get; set; } = 365;

    // ---- cap an toàn (free-tier quota + chống PDF/Excel phình) -------------

    /// <summary>Cap số dòng chi tiết (theo bảng/người/hành động) trả về và ghi vào file.</summary>
    public int MaxExportRows { get; set; } = 5000;

    /// <summary>Cap số dòng của khối hoạt động.</summary>
    public int MaxActionsPerReport { get; set; } = 200;

    // ---- định dạng file ----------------------------------------------------

    /// <summary>Khổ trang PDF: <c>A4</c> | <c>A3</c> | <c>Letter</c> (giá trị khác ⇒ fallback A4 + log warning ở §4).</summary>
    public string PdfPageSize { get; set; } = "A4";

    /// <summary>Tên đơn vị hiển thị ở header/footer PDF và sheet "Tổng quan".</summary>
    public string CompanyName { get; set; } = "TeamNexus";

    /// <summary>Format hiển thị ngày giờ trong file Excel.</summary>
    public string ExcelDateFormat { get; set; } = "dd/MM/yyyy HH:mm";

    /// <summary>Bật/tắt khối "Sức khoẻ AI" trong báo cáo (renderer §4 đọc cờ này — aggregator luôn tính).</summary>
    public bool IncludeHealthSection { get; set; } = true;

    /// <summary>
    /// Tường minh hoá cách đếm <c>overdue</c>: <c>true</c> (mặc định) = chỉ task **chưa** xong mới có thể
    /// quá hạn — khớp <c>ObserverSignalDetector</c>; <c>false</c> = task đã xong nhưng trễ hạn cũng được đếm.
    /// </summary>
    public bool ExcludeDoneOverdue { get; set; } = true;

    // ---- chuỗi thời gian cho Burndown/Velocity (Phase 13 §2) ----------------

    /// <summary>Cap số bucket của chuỗi thời gian theo ngày (khoảng dài hơn bị cắt từ đầu).</summary>
    public int MaxSeriesBuckets { get; set; } = 90;

    /// <summary>Khoảng từ số ngày này trở lên ⇒ gộp bucket theo **tuần** thay vì theo ngày.</summary>
    public int SeriesWeeklyThresholdDays { get; set; } = 60;

    // ---- derived -----------------------------------------------------------

    /// <summary>
    /// Snapshot ngưỡng/cap với clamp phòng thủ (config 0/âm ⇒ tối thiểu 1) để
    /// <see cref="ReportAggregator"/> không bao giờ chia 0 hay cắt sạch dữ liệu.
    /// </summary>
    public ReportThresholds ToThresholds()
    {
        var maxRangeDays = Math.Max(1, MaxRangeDays);

        return new ReportThresholds(
            MaxExportRows: Math.Max(1, MaxExportRows),
            MaxActionsPerReport: Math.Max(1, MaxActionsPerReport),
            MaxRangeDays: maxRangeDays,
            DefaultRangeDays: Math.Clamp(DefaultRangeDays, 1, maxRangeDays),
            ExcludeDoneOverdue: ExcludeDoneOverdue,
            MaxSeriesBuckets: Math.Clamp(MaxSeriesBuckets, 1, Math.Max(1, maxRangeDays)),
            SeriesWeeklyThresholdDays: Math.Clamp(SeriesWeeklyThresholdDays, 1, Math.Max(1, maxRangeDays)));
    }

    /// <summary>
    /// Cửa sổ báo cáo cho một request. Uỷ nhiệm toàn bộ cho
    /// <see cref="ReportThresholds.BuildRange(DateTimeOffset?, DateTimeOffset?, DateTimeOffset)"/> để chỉ
    /// có **một** công thức cửa sổ dùng cho cả endpoint (§5) lẫn harness.
    /// </summary>
    public ReportRange EffectiveRange(DateTimeOffset? from, DateTimeOffset? to, DateTimeOffset now)
        => ToThresholds().BuildRange(from, to, now);

    /// <summary>
    /// Parse <see cref="PdfPageSize"/> **strict** (chỉ <c>A4</c>/<c>A3</c>/<c>Letter</c>, không phân biệt
    /// hoa thường). Trả <c>false</c> cho giá trị lạ để caller (§4) log warning + fallback A4 — cố ý
    /// **không** ném lúc khởi động.
    /// </summary>
    public bool TryGetPdfPageSize(out string pageSize)
    {
        var normalized = PdfPageSize?.Trim();

        switch (normalized?.ToUpperInvariant())
        {
            case "A4":
                pageSize = "A4";
                return true;
            case "A3":
                pageSize = "A3";
                return true;
            case "LETTER":
                pageSize = "Letter";
                return true;
            default:
                pageSize = "A4";
                return false;
        }
    }
}
