using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Reporting.Contracts;

namespace TeamNexus.Modules.Reporting.Services;

/// <summary>
/// Implementation tạm thời của <see cref="IReportExportService"/> khi chưa có renderer PDF/Excel.
/// <para>
/// §3 (service đọc dữ liệu) không cần QuestPDF/ClosedXML, nhưng DI container **validate toàn bộ** service
/// graph khi host build (mặc định <c>ValidateOnBuild</c> trong Development) — thiếu implementation sẽ làm
/// **mọi** thao tác dùng host đổ vỡ, kể cả <c>dotnet ef migrations list</c>. Bản no-op này giữ graph hợp lệ
/// và chỉ bật cờ để §4 biết cần cắm implementation thật.
/// </para>
/// <para>
/// ⚠️ <b>§4 PHẢI đăng ký đè</b> <c>IReportExportService</c> bằng <c>ReportExportService</c> thật (QuestPDF +
/// ClosedXML) — khi đó bản no-op này không còn được dùng. Gọi nhầm bản no-op sẽ trả **500** với thông báo
/// rõ ràng ở <see cref="Build"/>, **không** im lặng trả file rỗng.
/// </para>
/// </summary>
public sealed class NoReportExportService : IReportExportService
{
    /// <summary>Đọc lúc khởi động để biết đang chạy bản no-op (một dòng log, không log mỗi request).</summary>
    public const string WarningMessage =
        "Reporting module: IReportExportService chưa có implementation — đăng ký renderer ở §4 (QuestPDF/ClosedXML).";

    private readonly ILogger<NoReportExportService> _logger;

    public NoReportExportService(ILogger<NoReportExportService> logger) => _logger = logger;

    public ReportExportResult Build(ReportSummary summary, string format, Options.ReportsOptions options)
    {
        _logger.LogError(
            "Reporting export thất bại: chưa có IReportExportService (format={Format}). Cần implementation §4 (QuestPDF/ClosedXML).",
            format);

        throw new NotSupportedException(
            "Report export is not available yet: the PDF/Excel renderer (Phase 6 §4) is not registered.");
    }
}
