using TeamNexus.Modules.Reporting.Contracts;
using TeamNexus.Modules.Reporting.Options;

namespace TeamNexus.Modules.Reporting.Services;

/// <summary>
/// Cổng render Excel báo cáo (Phase 6 §4.4). Hàm **thuần**: nhận <see cref="ReportSummary"/> đã tổng hợp
/// và trả <c>byte[]</c> (.xlsx trong RAM) — không DB, không file.
/// </summary>
public interface IReportExcelRenderer
{
    /// <param name="report">Báo cáo đã tổng hợp.</param>
    /// <param name="options">Format ngày, tên đơn vị, bật/tắt khối sức khoẻ AI…</param>
    byte[] Render(ReportSummary report, ReportsOptions options);
}
