using Microsoft.AspNetCore.Http;
using TeamNexus.Modules.Board.Services;

namespace TeamNexus.Modules.Reporting.Services;

/// <summary>
/// Xuất báo cáo đang bị tắt bằng cấu hình (<c>Reports:Enabled = false</c>) → <b>503</b> (Phase 6 §0 D20).
/// <para>
/// Cố ý khác 404/403: UI cần phân biệt "tính năng đang tắt" với "không có quyền / không tồn tại".
/// Kế thừa <see cref="BoardModuleException"/> nên <c>DomainExceptionFilter</c> của module Board map sẵn
/// thành <c>{ error }</c> + status — module Reporting không cần filter riêng (cùng cách
/// <c>AiProviderException</c> của Phase 3).
/// </para>
/// </summary>
public sealed class ReportingDisabledException : BoardModuleException
{
    public ReportingDisabledException()
        : base(StatusCodes.Status503ServiceUnavailable, "Reporting is disabled.")
    {
    }
}

/// <summary>
/// Tham số <c>format</c> thiếu hoặc không được hỗ trợ → <b>400</b> (Phase 6 §3.2).
/// Chỉ nhận <c>pdf</c>/<c>excel</c> (không phân biệt hoa thường); không đoán định dạng.
/// </summary>
public sealed class InvalidReportFormatException : BoardModuleException
{
    public InvalidReportFormatException(string? format)
        : base(
            StatusCodes.Status400BadRequest,
            $"Unknown report format '{format}'. Expected {Contracts.ReportExportFormats.Pdf} or {Contracts.ReportExportFormats.Excel}.")
    {
    }
}

/// <summary>
/// Khoảng thời gian không hợp lệ (<c>from</c> sau <c>to</c>) → <b>400</b> (Phase 6 §3.2).
/// <para>
/// Lưu ý: <c>ReportThresholds.BuildRange</c> (§1) **không** ném cho trường hợp này (nó kéo về 1 ngày để
/// không làm hỏng báo cáo) — validate là trách nhiệm của service, thực hiện **trước** khi gọi aggregator.
/// </para>
/// </summary>
public sealed class InvalidReportRangeException : BoardModuleException
{
    public InvalidReportRangeException()
        : base(StatusCodes.Status400BadRequest, "'from' must be earlier than or equal to 'to'.")
    {
    }
}

/// <summary>
/// Tham số query không parse được thành ISO-8601 (<c>from</c>/<c>to</c>) → <b>400</b> (Phase 6 §5.2).
/// <para>
/// Ném từ endpoint (§5) chứ không phải service: endpoint parse chuỗi query **strict** (giống
/// <c>ParseIsRead</c> Phase 5 §5.2) để trả đúng hợp đồng <c>{ error }</c> của dự án thay vì
/// ProblemDetails của framework. Định dạng <c>boardId</c>/<c>workspaceId</c> thì **cố ý** để framework bind
/// (sai ⇒ 400 ProblemDetails) — đừng parse tay để khỏi lệch contract.
/// </para>
/// </summary>
public sealed class InvalidReportParameterException : BoardModuleException
{
    public InvalidReportParameterException(string parameterName, string? value)
        : base(
            StatusCodes.Status400BadRequest,
            $"Unknown value for {parameterName} '{value}'. Expected ISO-8601 date-time.")
    {
    }
}
