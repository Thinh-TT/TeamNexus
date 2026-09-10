using TeamNexus.Modules.Reporting.Contracts;

namespace TeamNexus.Modules.Reporting.DTOs;

/// <summary>
/// DTO HTTP của module Reporting (Phase 6 §5.1): một chiều từ domain (<c>Contracts/</c>) sang hợp đồng
/// wire. Casing giữ **canonical** — <c>"board"</c>, <c>"OverdueTask"</c>, <c>"High"</c> — không lowercase
/// (tiền lệ <c>TaskPriority</c>/<c>ObserverRunStatus</c>); giá trị <c>null</c> giữ nguyên <c>null</c>
/// (UI render "—"), **không** đổi thành 0.
/// </summary>
public sealed record ReportPeriodResponse(DateTimeOffset From, DateTimeOffset To, int Days, bool Clamped);

/// <summary><c>Type</c> ∈ <see cref="ReportScopeTypes"/>; <c>BoardId</c>/<c>BoardName</c> chỉ có khi scope board.</summary>
public sealed record ReportScopeResponse(string Type, Guid? BoardId, string? BoardName);

public sealed record ReportProgressResponse(int Total, int Done, int Open, int Overdue, double DonePercent);

/// <summary>Các chỉ số <c>double?</c> là <c>null</c> khi mẫu số bằng 0 (≠ 0).</summary>
public sealed record ReportPerformanceResponse(
    int CreatedInRange,
    int CompletedInRange,
    double? AvgCompletionHours,
    double? AvgLeadTimeHours,
    double? OnTimeRate,
    double OverdueRate,
    double ThroughputPerWeek,
    int CompletedAtMissing);

public sealed record ReportBoardRowResponse(
    Guid BoardId,
    string BoardName,
    int Total,
    int Done,
    int Open,
    int Overdue,
    int CompletedInRange,
    double? AvgCompletionHours,
    double? OnTimeRate);

/// <summary><c>AssigneeId = null</c> là nhóm "Chưa gán" (<c>ReportUnassigned.Name</c>).</summary>
public sealed record ReportAssigneeRowResponse(
    Guid? AssigneeId,
    string AssigneeName,
    int Total,
    int Done,
    int Open,
    int Overdue,
    int CompletedInRange,
    double? AvgCompletionHours,
    double? OnTimeRate);

/// <summary><c>Action</c> là mã gốc (<c>TaskCreated</c>…); nhãn tiếng Việt do FE/PDF/Excel tự suy ra.</summary>
public sealed record ReportActionRowResponse(string Action, int Count);

public sealed record ReportActivityResponse(
    int TotalActions,
    IReadOnlyList<ReportActionRowResponse> ByAction,
    int ActiveUsers,
    double ActionsPerDay);

public sealed record ReportSeverityRowResponse(string Severity, int Count);

public sealed record ReportSignalTypeRowResponse(string Type, int Count);

public sealed record ReportHealthResponse(
    int RunsScanned,
    IReadOnlyList<ReportSignalTypeRowResponse> SignalsByType,
    IReadOnlyList<ReportSeverityRowResponse> FindingsBySeverity);

public sealed record ReportTruncationResponse(bool RowCapReached, int MaxRows);

/// <summary>
/// Báo cáo đầy đủ trả cho <c>GET /api/workspaces/{workspaceId}/reports/summary</c>.
/// <para>
/// <c>MetricDefinitions</c> giữ **nguyên** map từ domain (D9/D35): đây là nguồn mô tả chỉ số duy nhất dùng
/// chung cho JSON, PDF và Excel ⇒ serialize thành JSON object <c>{ "total": "…", … }</c>.
/// </para>
/// </summary>
public sealed record ReportSummaryResponse(
    Guid WorkspaceId,
    string WorkspaceName,
    ReportScopeResponse Scope,
    ReportPeriodResponse Period,
    DateTimeOffset GeneratedAt,
    ReportProgressResponse Progress,
    ReportPerformanceResponse Performance,
    IReadOnlyList<ReportBoardRowResponse> ByBoard,
    IReadOnlyList<ReportAssigneeRowResponse> ByAssignee,
    ReportActivityResponse Activity,
    ReportHealthResponse Health,
    IReadOnlyDictionary<string, string> MetricDefinitions,
    ReportTruncationResponse Truncated)
{
    /// <summary>Domain → HTTP (một chiều, không lặp khởi tạo rải rác — tiền lệ <c>ObserverScanResponse.From</c>).</summary>
    public static ReportSummaryResponse From(ReportSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new ReportSummaryResponse(
            summary.WorkspaceId,
            summary.WorkspaceName,
            new ReportScopeResponse(summary.Scope.Type, summary.Scope.BoardId, summary.Scope.BoardName),
            new ReportPeriodResponse(summary.Period.From, summary.Period.To, summary.Period.Days, summary.Period.Clamped),
            summary.GeneratedAt,
            new ReportProgressResponse(
                summary.Progress.Total,
                summary.Progress.Done,
                summary.Progress.Open,
                summary.Progress.Overdue,
                summary.Progress.DonePercent),
            new ReportPerformanceResponse(
                summary.Performance.CreatedInRange,
                summary.Performance.CompletedInRange,
                summary.Performance.AvgCompletionHours,
                summary.Performance.AvgLeadTimeHours,
                summary.Performance.OnTimeRate,
                summary.Performance.OverdueRate,
                summary.Performance.ThroughputPerWeek,
                summary.Performance.CompletedAtMissing),
            summary.ByBoard.Select(row => new ReportBoardRowResponse(
                row.BoardId,
                row.BoardName,
                row.Total,
                row.Done,
                row.Open,
                row.Overdue,
                row.CompletedInRange,
                row.AvgCompletionHours,
                row.OnTimeRate)).ToList(),
            summary.ByAssignee.Select(row => new ReportAssigneeRowResponse(
                row.AssigneeId,
                row.AssigneeName,
                row.Total,
                row.Done,
                row.Open,
                row.Overdue,
                row.CompletedInRange,
                row.AvgCompletionHours,
                row.OnTimeRate)).ToList(),
            new ReportActivityResponse(
                summary.Activity.TotalActions,
                summary.Activity.ByAction.Select(row => new ReportActionRowResponse(row.Action, row.Count)).ToList(),
                summary.Activity.ActiveUsers,
                summary.Activity.ActionsPerDay),
            new ReportHealthResponse(
                summary.Health.RunsScanned,
                summary.Health.SignalsByType.Select(row => new ReportSignalTypeRowResponse(row.Type, row.Count)).ToList(),
                summary.Health.FindingsBySeverity.Select(row => new ReportSeverityRowResponse(row.Severity, row.Count)).ToList()),
            summary.MetricDefinitions,
            new ReportTruncationResponse(summary.Truncated.RowCapReached, summary.Truncated.MaxRows));
    }
}

/// <summary>Một lựa chọn board cho bộ lọc báo cáo (<c>GET .../reports/boards</c>).</summary>
public sealed record ReportBoardOptionResponse(Guid Id, string Name, int TaskCount, int IsDoneColumns)
{
    public static ReportBoardOptionResponse From(ReportBoardOption option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return new ReportBoardOptionResponse(option.Id, option.Name, option.TaskCount, option.IsDoneColumns);
    }
}
