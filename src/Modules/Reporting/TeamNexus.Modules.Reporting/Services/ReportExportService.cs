using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Reporting.Contracts;
using TeamNexus.Modules.Reporting.Options;

namespace TeamNexus.Modules.Reporting.Services;

/// <summary>
/// Cổng xuất báo cáo ra file (Phase 6 §4.5): `<c>ReportSummary</c>` → <see cref="ReportExportResult"/>.
/// <para>
/// Tách interface khỏi implementation để <c>ReportService</c> (§3) chỉ phụ thuộc hợp đồng, không kéo
/// QuestPDF/ClosedXML vào tầng đọc dữ liệu.
/// </para>
/// </summary>
public interface IReportExportService
{
    /// <param name="summary">Báo cáo đã tổng hợp (không được null).</param>
    /// <param name="format">Đã chuẩn hoá bằng <see cref="ReportExportFormats.Normalize"/> (pdf|excel).</param>
    /// <param name="options">Cấu hình định dạng file (khổ trang, tên đơn vị, format ngày…).</param>
    ReportExportResult Build(ReportSummary summary, string format, ReportsOptions options);
}

/// <summary>
/// Implementation mặc định của <see cref="IReportExportService"/> (đăng ký trong <c>AddReportingModule</c>):
/// chọn renderer theo định dạng, đặt tên file ASCII và gói metadata cho tầng HTTP (§5) — **không** chạm DB,
/// **không** ghi file.
/// <para>
/// Cả PDF và Excel render trên **cùng** <see cref="ReportSummary"/> mà <c>IReportService</c> đã dựng cho
/// <c>GET .../reports/summary</c> ⇒ JSON, PDF và Excel không thể lệch số liệu.
/// </para>
/// </summary>
public sealed class ReportExportService : IReportExportService
{
    private readonly IReportPdfRenderer _pdfRenderer;
    private readonly IReportExcelRenderer _excelRenderer;
    private readonly ILogger<ReportExportService> _logger;

    public ReportExportService(
        IReportPdfRenderer pdfRenderer,
        IReportExcelRenderer excelRenderer,
        ILogger<ReportExportService> logger)
    {
        _pdfRenderer = pdfRenderer;
        _excelRenderer = excelRenderer;
        _logger = logger;
    }

    public ReportExportResult Build(ReportSummary summary, string format, ReportsOptions options)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(options);

        // `format` đã được chuẩn hoá ở §3 (ReportExportFormats.Normalize) trước khi gọi vào đây, nhưng vẫn
        // kiểm tra phòng thủ để không bao giờ trả file sai định dạng khi service được gọi trực tiếp.
        var normalized = ReportExportFormats.Normalize(format)
                         ?? throw new ArgumentOutOfRangeException(
                             nameof(format), format, "Format must be normalized to 'pdf' or 'excel' before rendering.");

        var extension = ReportExportFormats.FileExtension(normalized);
        var scopeLabel = summary.Scope.Type == ReportScopeTypes.Board && !string.IsNullOrWhiteSpace(summary.Scope.BoardName)
            ? summary.Scope.BoardName
            : summary.WorkspaceName;

        var content = normalized switch
        {
            ReportExportFormats.Pdf => _pdfRenderer.Render(summary, options),
            ReportExportFormats.Excel => _excelRenderer.Render(summary, options),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported report format."),
        };

        var fileName = ReportFileName.Build(scopeLabel, extension, summary.GeneratedAt);
        var rows = CountDetailRows(summary);

        _logger.LogInformation(
            "Reporting export: format={Format}, scope={ScopeType}, rows={Rows}, bytes={Bytes}, capped={Capped}, file={FileName}.",
            normalized,
            summary.Scope.Type,
            rows,
            content.Length,
            summary.Truncated.RowCapReached,
            fileName);

        return new ReportExportResult(
            Content: content,
            FileName: fileName,
            ContentType: ReportExportFormats.ContentType(normalized),
            Rows: rows,
            RowCapReached: summary.Truncated.RowCapReached,
            PeriodLabel: summary.Period.Label,
            ScopeLabel: scopeLabel);
    }

    /// <summary>
    /// Số dòng chi tiết đã ghi vào file (Phase 6 §4 — D25): tổng dòng của các bảng chia nhỏ
    /// (theo bảng / theo người / hành động / tín hiệu / mức độ). Dùng chung cho cả 2 định dạng nên
    /// con số này có nghĩa thống nhất.
    /// </summary>
    private static int CountDetailRows(ReportSummary summary)
        => summary.ByBoard.Count
           + summary.ByAssignee.Count
           + summary.Activity.ByAction.Count
           + summary.Health.SignalsByType.Count
           + summary.Health.FindingsBySeverity.Count;
}
