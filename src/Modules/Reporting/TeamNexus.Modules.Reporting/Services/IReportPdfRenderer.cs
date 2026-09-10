using TeamNexus.Modules.Reporting.Contracts;
using TeamNexus.Modules.Reporting.Options;

namespace TeamNexus.Modules.Reporting.Services;

/// <summary>
/// Cổng render PDF báo cáo (Phase 6 §4.3). Hàm **thuần**: chỉ nhận <see cref="ReportSummary"/> đã tổng hợp
/// (một nguồn số liệu duy nhất với JSON/Excel) và trả <c>byte[]</c> — không DB, không file,
/// không <c>DateTime.Now</c> (dùng <c>report.GeneratedAt</c>).
/// </summary>
public interface IReportPdfRenderer
{
    /// <param name="report">Báo cáo đã tổng hợp.</param>
    /// <param name="options">Khổ trang, tên đơn vị, bật/tắt khối sức khoẻ AI…</param>
    byte[] Render(ReportSummary report, ReportsOptions options);
}
