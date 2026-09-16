using TeamNexus.Modules.Board.Services;
using TeamNexus.Modules.Reporting.Contracts;

namespace TeamNexus.Modules.Reporting.Services;

/// <summary>
/// Cổng đọc dữ liệu báo cáo (Phase 6 §3) — **nơi duy nhất** module Reporting chạm database.
/// <para>
/// Mọi method ở đây là **read-only**: chỉ nạp dữ liệu bounded rồi chiếu vào snapshot §1 và gọi
/// <see cref="ReportAggregator.Build"/>. Không ghi bảng nào (kể cả <c>activity_logs</c>/<c>notifications</c>),
/// không đi qua Accountability Layer, không đọc/ghi file — khớp bất biến Phase 6 §0 D2/D16.
/// </para>
/// <para>
/// Quyền: **Manager/Admin** của workspace (Member ⇒ 403, workspace lạ ⇒ 404) cho cả 3 method, enforce bên
/// trong service qua <c>IWorkspaceAccess.RequireManagerAsync</c> (đúng pattern Phase 5 §5.3).
/// </para>
/// </summary>
public interface IReportService
{
    /// <summary>
    /// Báo cáo tiến độ + hiệu suất của một workspace (lọc tuỳ chọn theo board, cửa sổ thời gian tuỳ chọn).
    /// Trả **domain model** <see cref="ReportSummary"/> — tầng HTTP (§5) map sang DTO, tầng renderer (§4)
    /// dùng trực tiếp, nên đường JSON/PDF/Excel luôn cùng một nguồn số liệu.
    /// </summary>
    /// <param name="from">Bắt đầu cửa sổ; <c>null</c> ⇒ mặc định <c>Reports:DefaultRangeDays</c>.</param>
    /// <param name="to">Kết thúc cửa sổ; <c>null</c> ⇒ "bây giờ". Không bao giờ vượt quá hiện tại.</param>
    /// <exception cref="ReportingDisabledException">Reporting đang tắt (503).</exception>
    /// <exception cref="InvalidReportRangeException"><paramref name="from"/> sau <paramref name="to"/> (400).</exception>
    /// <exception cref="ForbiddenException">Caller không phải Manager/Admin (403).</exception>
    /// <exception cref="NotFoundException">Workspace/board không thấy hoặc không thuộc quyền (404).</exception>
    Task<ReportSummary> GetSummaryAsync(
        Guid workspaceId,
        Guid? boardId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Dựng báo cáo rồi render ra file trong RAM (PDF/Excel) theo <paramref name="format"/>.
    /// Cùng validate/quyền như <see cref="GetSummaryAsync"/>, cộng thêm kiểm tra định dạng.
    /// </summary>
    /// <exception cref="InvalidReportFormatException"><paramref name="format"/> thiếu/không hỗ trợ (400).</exception>
    Task<ReportExportResult> BuildExportAsync(
        Guid workspaceId,
        Guid? boardId,
        string format,
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>Danh sách board của workspace (kèm số task + số cột done) để đổ bộ lọc báo cáo.</summary>
    Task<IReadOnlyList<ReportBoardOption>> ListBoardsAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Chuỗi thời gian <c>openTasks</c>/<c>completions</c>/<c>creations</c> cho biểu đồ Burndown/Velocity
    /// (Phase 13 §2).
    /// <para>
    /// Cùng validate/quyền như <see cref="GetSummaryAsync"/> (**Manager+**): dữ liệu này là một lát cắt của
    /// báo cáo nên không có lý do gì cho nó một bề mặt quyền khác.
    /// </para>
    /// </summary>
    /// <param name="tzOffsetMinutes">
    /// Múi giờ người xem (UTC + giá trị này), đơn vị phút. <c>null</c> ⇒ <c>0</c> (UTC); giá trị ngoài
    /// <c>±840</c> bị clamp (không 400 — đây là knob UI) và giá trị thực dùng được trả lại trong response.
    /// </param>
    /// <exception cref="ReportingDisabledException">Reporting đang tắt (503).</exception>
    /// <exception cref="InvalidReportRangeException"><paramref name="from"/> sau <paramref name="to"/> (400).</exception>
    /// <exception cref="ForbiddenException">Caller không phải Manager/Admin (403).</exception>
    /// <exception cref="NotFoundException">Workspace/board không thấy hoặc không thuộc quyền (404).</exception>
    Task<ReportProgressSeries> GetProgressSeriesAsync(
        Guid workspaceId,
        Guid? boardId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? tzOffsetMinutes,
        Guid userId,
        CancellationToken ct = default);
}
