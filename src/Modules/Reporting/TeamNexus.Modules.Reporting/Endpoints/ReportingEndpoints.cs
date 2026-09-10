using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Modules.Reporting.DTOs;
using TeamNexus.Modules.Reporting.Services;

namespace TeamNexus.Modules.Reporting.Endpoints;

/// <summary>
/// Endpoint group của module Reporting (Phase 6 §5.2) dưới <c>/api/workspaces/{workspaceId}/reports</c>.
/// <para>
/// Ba route (tổng quan báo cáo, xuất file, danh sách board) đều là <b>GET</b> — đọc, idempotent — nên
/// **không** gắn <c>AntiforgeryValidationEndpointFilter</c> (Phase 6 §0 D12); dùng GET còn để
/// <c>httpClient</c> của frontend tự refresh 401 (tải file qua axios `responseType: 'blob'`).
/// </para>
/// <para>
/// Tầng này **mỏng có chủ ý** (D30): toàn bộ validate nghiệp vụ (503 khi tắt, 403/404 quyền, 400 format,
/// 400 khoảng thời gian, 404 board lạ) đã nằm trong <c>IReportService</c> (§3 đã verify) và được
/// <c>DomainExceptionFilter</c> map thành <c>{ "error": "…" }</c>. Endpoint chỉ: parse tham số → gọi
/// service → map DTO → set header phụ.
/// </para>
/// </summary>
public static class ReportingEndpoints
{
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/reports")
            .WithTags("Reports")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapGet("/summary", GetSummaryAsync).RequireAuthorization();
        group.MapGet("/export", ExportAsync).RequireAuthorization();
        group.MapGet("/boards", ListBoardsAsync).RequireAuthorization();

        return endpoints;
    }

    // ---- handlers ----------------------------------------------------------

    private static async Task<IResult> GetSummaryAsync(
        Guid workspaceId,
        Guid? boardId,
        string? from,
        string? to,
        HttpContext http,
        IReportService reports,
        CancellationToken ct)
    {
        var summary = await reports.GetSummaryAsync(
            workspaceId,
            boardId,
            ReportingEndpointHelpers.ParseIso8601(from, "from"),
            ReportingEndpointHelpers.ParseIso8601(to, "to"),
            http.RequireUserId(),
            ct);

        return Results.Ok(ReportSummaryResponse.From(summary));
    }

    /// <summary>
    /// Trả file **trong RAM** (`FileContentResult`) — không lưu disk/blob (Phase 6 §0 D11).
    /// <para>
    /// Ba header <c>X-Report-*</c> là **thông tin phụ**: UI/mobile có thể đọc để biết kỳ báo cáo, số dòng
    /// chi tiết và dữ liệu có bị cắt bởi cap hay không, nhưng **không** được phụ thuộc chúng để hiển thị
    /// đúng (tên file vẫn nằm trong <c>Content-Disposition</c>, số liệu vẫn nằm trong file).
    /// </para>
    /// </summary>
    private static async Task<IResult> ExportAsync(
        Guid workspaceId,
        string? format,
        Guid? boardId,
        string? from,
        string? to,
        HttpContext http,
        IReportService reports,
        CancellationToken ct)
    {
        var result = await reports.BuildExportAsync(
            workspaceId,
            boardId,
            format ?? string.Empty,
            ReportingEndpointHelpers.ParseIso8601(from, "from"),
            ReportingEndpointHelpers.ParseIso8601(to, "to"),
            http.RequireUserId(),
            ct);

        var headers = http.Response.Headers;
        headers["X-Report-Row-Cap-Reached"] = result.RowCapReached ? "true" : "false";
        headers["X-Report-Rows"] = result.Rows.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // ⚠️ Header HTTP chỉ nhận ASCII: nhãn kỳ của domain dùng en dash "–" (0x2013) cho bản in PDF/Excel,
        // nếu đưa nguyên vào header thì Kestrel ném và **mọi** request export thành 500 (đã bắt được nhờ
        // harness §5). Vì vậy header dùng bản ASCII hoá; bản có dấu vẫn nằm trong file.
        headers["X-Report-Period"] = Ascii(result.PeriodLabel);

        return Results.File(result.Content, result.ContentType, fileDownloadName: result.FileName);
    }

    /// <summary>
    /// Hạ nhãn về ASCII để đưa vào header (giữ en dash/em dash thành <c>-</c>, bỏ dấu tiếng Việt còn sót).
    /// </summary>
    private static string Ascii(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (ch is >= '\u0300' and <= '\u036F')
            {
                continue;
            }

            builder.Append(ch switch
            {
                '\u2013' or '\u2014' => '-',
                _ => ch is >= ' ' and <= '~' ? ch : '-',
            });
        }

        return builder.ToString();
    }

    private static async Task<IResult> ListBoardsAsync(
        Guid workspaceId,
        HttpContext http,
        IReportService reports,
        CancellationToken ct)
    {
        var boards = await reports.ListBoardsAsync(workspaceId, http.RequireUserId(), ct);

        return Results.Ok(boards.Select(ReportBoardOptionResponse.From).ToList());
    }
}
