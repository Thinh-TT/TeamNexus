namespace TeamNexus.Modules.Reporting.Services;

/// <summary>
/// Snapshot dữ liệu thô của **một** workspace tại thời điểm dựng báo cáo (Phase 6 §1.1).
/// <para>
/// Toàn bộ record trong file này là ranh giới giữa tầng đọc DB (§3 <c>ReportService</c>) và tầng tính
/// toán thuần (<see cref="ReportAggregator"/>): loader chỉ việc chiếu dữ liệu vào đây, aggregator
/// không cần biết gì về EF Core. Nhờ vậy nhóm verify "aggregation thuần" chạy được **không** DB/HTTP.
/// </para>
/// <para>
/// Cố ý KHÔNG tái dùng <c>ObserverColumnSnapshot</c>/<c>ObserverTaskSnapshot</c> của module Ai
/// (Phase 6 §0 D4: Reporting không tham chiếu module Ai) — hình dạng có thể giống nhưng đây là type
/// riêng của Reporting, và sẽ tiến hoá theo nhu cầu báo cáo, không theo nhu cầu phát hiện tín hiệu.
/// </para>
/// </summary>
public sealed record ReportBoardSnapshot(Guid BoardId, string Name);

/// <summary>Cột Kanban kèm cờ <c>is_done</c> — nguồn duy nhất để biết một task đã hoàn thành.</summary>
public sealed record ReportColumnSnapshot(Guid ColumnId, Guid BoardId, string Name, bool IsDone);

/// <summary>
/// Một task đã được chiếu sẵn cho aggregator.
/// <para>
/// <see cref="BoardName"/>/<see cref="ColumnName"/> được loader nạp kèm để báo cáo có ngữ cảnh đọc được
/// (không chỉ GUID) mà **không** phát sinh truy vấn N+1.
/// </para>
/// </summary>
public sealed record ReportTaskSnapshot(
    Guid TaskId,
    Guid BoardId,
    string BoardName,
    Guid ColumnId,
    string ColumnName,
    bool IsDoneColumn,
    string Title,
    Guid? AssigneeId,
    string? AssigneeName,
    string? Priority,
    DateTimeOffset? DueDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>Số sự kiện theo một giá trị <c>activity_logs.action</c> (trong cửa sổ báo cáo).</summary>
public sealed record ReportActionCount(string Action, int Count);

/// <summary>
/// Sự kiện trong cửa sổ, gom theo <c>action</c>, kèm số người dùng riêng biệt.
/// <para>
/// Đây là đầu vào của khối <c>activity</c>: loader chỉ SELECT <c>action</c>, <c>user_id</c>,
/// <c>created_at</c> (không đọc toàn văn <c>payload</c>) rồi tự gom — nhờ vậy <c>activeUsers</c> tính
/// được ngay tại tầng đọc mà **không** cần aggregator biết về user.
/// </para>
/// </summary>
public sealed record ReportActivitySnapshot(
    int TotalActions,
    int ActiveUsers,
    IReadOnlyList<ReportActionCount> ByAction);

/// <summary>
/// Một cặp <c>(type, severity)</c> đếm được từ <c>ai_observer_runs.summary.findings[]</c>.
/// Loader §3 parse jsonb **phòng thủ** (JSON hỏng ⇒ bỏ qua row) rồi mới đưa vào đây ⇒ aggregator
/// không bao giờ phải xử lý <c>JsonElement</c>.
/// </summary>
public sealed record ReportFindingCount(string Type, string Severity, int Count);

/// <summary>
/// Toàn bộ đầu vào của <see cref="ReportAggregator.Build"/>: dữ liệu workspace đã chiếu sẵn, cửa sổ
/// thời gian đã clamp (<see cref="Contracts.ReportRange"/>) và mốc <c>Now</c> để tính quá hạn.
/// <para>
/// Mọi danh sách có thể <c>null</c> từ loader (lỗi/không có dữ liệu) — <see cref="ReportAggregator"/>
/// coi như rỗng, không bao giờ ném vì snapshot thiếu mảng.
/// </para>
/// </summary>
public sealed record ReportWorkspaceSnapshot(
    Guid WorkspaceId,
    string WorkspaceName,
    Contracts.ReportRange Range,
    IReadOnlyList<ReportBoardSnapshot> Boards,
    IReadOnlyList<ReportColumnSnapshot> Columns,
    IReadOnlyList<ReportTaskSnapshot> Tasks,
    ReportActivitySnapshot Activity,
    IReadOnlyList<ReportFindingCount> Findings,
    int RunsScanned,
    DateTimeOffset Now);
