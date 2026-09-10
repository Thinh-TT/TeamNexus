using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Modules.Reporting.Contracts;
using TeamNexus.Modules.Reporting.Options;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Reporting.Services;

/// <summary>
/// Loader + điều phối báo cáo (Phase 6 §3): validate → nạp dữ liệu **bounded** → chiếu vào snapshot §1 →
/// <see cref="ReportAggregator.Build"/> → (đường export) renderer.
/// <para>
/// Class này là **nơi duy nhất** module Reporting chạm database, và chỉ để đọc: mọi truy vấn
/// <c>AsNoTracking()</c>, không <c>SaveChanges</c>/<c>ExecuteUpdate</c>/<c>ExecuteDelete</c>, không
/// transaction ghi, không file. Một request = một batch truy vấn cố định (không N+1).
/// </para>
/// </summary>
public sealed class ReportService : IReportService
{
    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IReportExportService _export;
    private readonly ReportsOptions _options;
    private readonly ILogger<ReportService> _logger;

    public ReportService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IReportExportService export,
        IOptions<ReportsOptions> options,
        ILogger<ReportService> logger)
    {
        _db = db;
        _access = access;
        _export = export;
        _options = options.Value;
        _logger = logger;
    }

    // ---- public API --------------------------------------------------------

    public async Task<ReportSummary> GetSummaryAsync(
        Guid workspaceId,
        Guid? boardId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid userId,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // Một mốc thời gian cho CẢ request: Period.To, quy tắc quá hạn và GeneratedAt phải nhất quán
        // (nếu lấy DateTimeOffset.UtcNow nhiều lần thì "thời điểm lập báo cáo" lệch nhau vài ms).
        var now = DateTimeOffset.UtcNow;

        EnsureEnabled();
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var range = ValidateRange(from, to, now);
        var boardName = await ResolveBoardScopeAsync(workspaceId, boardId, ct);

        var snapshot = await LoadSnapshotAsync(workspaceId, boardId, boardName, range, now, ct);
        var report = ReportAggregator.Build(snapshot, _options.ToThresholds());

        stopwatch.Stop();
        LogSummary(report, stopwatch.ElapsedMilliseconds);

        return report;
    }

    public async Task<ReportExportResult> BuildExportAsync(
        Guid workspaceId,
        Guid? boardId,
        string format,
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid userId,
        CancellationToken ct = default)
    {
        EnsureEnabled();

        // Định dạng được validate ngay sau khi biết service đang bật, TRƯỚC khi tốn truy vấn nào.
        var normalizedFormat = ReportExportFormats.Normalize(format)
                               ?? throw new InvalidReportFormatException(format);

        var report = await GetSummaryAsync(workspaceId, boardId, from, to, userId, ct);

        return _export.Build(report, normalizedFormat, _options);
    }

    public async Task<IReadOnlyList<ReportBoardOption>> ListBoardsAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken ct = default)
    {
        EnsureEnabled();
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var boards = await _db.Boards
            .AsNoTracking()
            .Where(b => b.WorkspaceId == workspaceId)
            .Select(b => new { b.Id, b.Name })
            .ToListAsync(ct);

        if (boards.Count == 0)
        {
            return [];
        }

        var boardIds = boards.Select(b => b.Id).ToList();

        var taskCounts = await _db.Tasks
            .AsNoTracking()
            .Where(t => boardIds.Contains(t.BoardId))
            .GroupBy(t => t.BoardId)
            .Select(g => new { BoardId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var doneColumns = await _db.BoardColumns
            .AsNoTracking()
            .Where(c => boardIds.Contains(c.BoardId) && c.IsDone)
            .GroupBy(c => c.BoardId)
            .Select(g => new { BoardId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var tasksByBoard = taskCounts.ToDictionary(x => x.BoardId, x => x.Count);
        var doneByBoard = doneColumns.ToDictionary(x => x.BoardId, x => x.Count);

        return boards
            .Select(b => new ReportBoardOption(
                b.Id,
                b.Name,
                tasksByBoard.GetValueOrDefault(b.Id),
                doneByBoard.GetValueOrDefault(b.Id)))
            .OrderBy(b => b.Name, StringComparer.Ordinal)
            .ThenBy(b => b.Id)
            .ToList();
    }

    // ---- bước 1–5: validate ------------------------------------------------

    /// <summary>Fail nhanh khi tính năng tắt — **trước** mọi truy vấn (kể cả truy vấn quyền).</summary>
    private void EnsureEnabled()
    {
        if (!_options.Enabled)
        {
            throw new ReportingDisabledException();
        }
    }

    /// <summary>
    /// Cửa sổ thời gian: từ chối khoảng ngược thứ tự (400) rồi để §1 clamp/mặc định hoá
    /// (<c>to</c> tương lai bị kéo về hiện tại, khoảng quá dài bị cắt + cờ <c>Clamped</c>).
    /// </summary>
    private ReportRange ValidateRange(DateTimeOffset? from, DateTimeOffset? to, DateTimeOffset now)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            throw new InvalidReportRangeException();
        }

        return _options.EffectiveRange(from, to, now);
    }

    /// <summary>
    /// Scope board: trả tên board khi hợp lệ, <c>null</c> khi báo cáo toàn workspace.
    /// Board không thấy / đã xoá mềm / thuộc workspace khác ⇒ 404 (không lộ sự tồn tại của board).
    /// </summary>
    private async Task<string?> ResolveBoardScopeAsync(Guid workspaceId, Guid? boardId, CancellationToken ct)
    {
        if (!boardId.HasValue)
        {
            return null;
        }

        var board = await _db.Boards
            .AsNoTracking()
            .Where(b => b.Id == boardId.Value && b.WorkspaceId == workspaceId)
            .Select(b => new { b.Name })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Board not found.");

        return board.Name;
    }

    // ---- bước 6: nạp dữ liệu bounded ---------------------------------------

    /// <summary>
    /// Nạp đúng những gì aggregator cần, mỗi bảng **một** truy vấn (không N+1), không đọc
    /// <c>activity_logs.payload</c> và không đọc nội dung AI ngoài <c>summary</c> của run.
    /// </summary>
    private async Task<ReportWorkspaceSnapshot> LoadSnapshotAsync(
        Guid workspaceId,
        Guid? boardId,
        string? boardName,
        ReportRange range,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var workspaceName = await _db.Workspaces
            .AsNoTracking()
            .Where(w => w.Id == workspaceId)
            .Select(w => w.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var boards = await _db.Boards
            .AsNoTracking()
            .Where(b => b.WorkspaceId == workspaceId && (boardId == null || b.Id == boardId.Value))
            .Select(b => new ReportBoardSnapshot(b.Id, b.Name))
            .ToListAsync(ct);

        var boardIds = boards.Select(b => b.BoardId).ToList();

        var columns = await _db.BoardColumns
            .AsNoTracking()
            .Where(c => boardIds.Contains(c.BoardId))
            .Select(c => new ReportColumnSnapshot(c.Id, c.BoardId, c.Name, c.IsDone))
            .ToListAsync(ct);

        // Query filter của BoardTask đã loại task soft-delete và task thuộc board soft-delete
        // (BoardTaskConfiguration): báo cáo không phải tự lọc lại.
        var tasks = await _db.Tasks
            .AsNoTracking()
            .Where(t => boardIds.Contains(t.BoardId))
            .Select(t => new ReportTaskSnapshot(
                t.Id,
                t.BoardId,
                t.Board!.Name,
                t.ColumnId,
                t.Column!.Name,
                t.Column != null && t.Column.IsDone,
                t.Title,
                t.AssigneeId,
                t.Assignee != null ? t.Assignee.DisplayName : null,
                t.Priority != null ? t.Priority.ToString() : null,
                t.DueDate,
                t.CreatedAt,
                t.UpdatedAt,
                t.CompletedAt))
            .ToListAsync(ct);

        var activity = await LoadActivityAsync(workspaceId, boardId, range, ct);
        var (findings, runsScanned) = await LoadObserverHealthAsync(workspaceId, range, ct);

        _ = boardName; // tên board đã nằm trong ReportBoardSnapshot; giữ tham số cho log/scope ở §5

        return new ReportWorkspaceSnapshot(
            workspaceId,
            workspaceName,
            range,
            boards,
            columns,
            tasks,
            activity,
            findings,
            runsScanned,
            now);
    }

    /// <summary>
    /// Khối hoạt động từ <c>activity_logs</c> trong cửa sổ (2 truy vấn gom tại DB).
    /// <para>
    /// Khi scope là một board: giữ sự kiện của board đó **và** sự kiện cấp workspace
    /// (<c>board_id IS NULL</c>) — sự kiện không gắn board nào vẫn thuộc workspace đang báo cáo.
    /// </para>
    /// <para>
    /// <c>activeUsers</c> là số người dùng **khác nhau trong cả cửa sổ** (không phải tổng của
    /// count-distinct theo từng action — tổng đó đếm trùng người xuất hiện ở nhiều loại hành động).
    /// Vì vậy nó được tính bằng một truy vấn <c>Distinct().Count()</c> riêng, không phụ thuộc khả năng
    /// dịch <c>Distinct</c> trong <c>GroupBy</c> của provider.
    /// </para>
    /// </summary>
    private async Task<ReportActivitySnapshot> LoadActivityAsync(
        Guid workspaceId,
        Guid? boardId,
        ReportRange range,
        CancellationToken ct)
    {
        var query = _db.Activities
            .AsNoTracking()
            .Where(a => a.WorkspaceId == workspaceId
                        && a.CreatedAt >= range.From
                        && a.CreatedAt <= range.To);

        if (boardId.HasValue)
        {
            query = query.Where(a => a.BoardId == boardId.Value || a.BoardId == null);
        }

        var counts = await query
            .GroupBy(a => a.Action)
            .Select(g => new { Action = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var activeUsers = await query
            .Where(a => a.UserId != null)
            .Select(a => a.UserId)
            .Distinct()
            .CountAsync(ct);

        var byAction = counts
            .Select(x => new ReportActionCount(x.Action, x.Count))
            .ToList();

        return new ReportActivitySnapshot(
            TotalActions: byAction.Sum(a => a.Count),
            ActiveUsers: activeUsers,
            ByAction: byAction);
    }

    /// <summary>
    /// Khối "sức khoẻ dự án" từ các run Observer <c>Completed</c> trong cửa sổ. Parse <c>summary</c>
    /// **phòng thủ**: JSON hỏng/thiếu key ⇒ bỏ qua row đó (không throw, không tính vào
    /// <c>runsScanned</c>) — cùng tinh thần <c>ObserverService.ReadInt/ReadBool</c>.
    /// </summary>
    private async Task<(List<ReportFindingCount> Findings, int RunsScanned)> LoadObserverHealthAsync(
        Guid workspaceId,
        ReportRange range,
        CancellationToken ct)
    {
        var summaries = await _db.AiObserverRuns
            .AsNoTracking()
            .Where(r => r.WorkspaceId == workspaceId
                        && r.Status == ObserverRunStatus.Completed
                        && r.StartedAt >= range.From
                        && r.StartedAt <= range.To)
            .Select(r => r.Summary)
            .ToListAsync(ct);

        var findings = new List<ReportFindingCount>();
        var runsScanned = 0;

        foreach (var summary in summaries)
        {
            if (ReadRunFindings(summary, findings))
            {
                runsScanned++;
            }
        }

        return (findings, runsScanned);
    }

    /// <summary>
    /// Đọc <c>signalsByType</c> + <c>findings[]</c> của một run vào <paramref name="findings"/>.
    /// Trả <c>true</c> khi summary đọc được (kể cả không có key nào).
    /// </summary>
    private static bool ReadRunFindings(string? summary, List<ReportFindingCount> findings)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(summary);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (document.RootElement.TryGetProperty("signalsByType", out var signals)
                && signals.ValueKind == JsonValueKind.Object)
            {
                foreach (var signal in signals.EnumerateObject())
                {
                    if (signal.Value.ValueKind == JsonValueKind.Number && signal.Value.TryGetInt32(out var count))
                    {
                        findings.Add(new ReportFindingCount(signal.Name, ReportSeverities.Unknown, count));
                    }
                }
            }

            if (document.RootElement.TryGetProperty("findings", out var items)
                && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var type = ReadString(item, "type") ?? ReportSeverities.Unknown;
                    var severity = ReportSeverities.Normalize(ReadString(item, "severity"));

                    findings.Add(new ReportFindingCount(type, severity, 1));
                }
            }

            return true;
        }
        catch (JsonException ex)
        {
            // Summary hỏng không được làm hỏng báo cáo: bỏ qua run này.
            _ = ex;
            return false;
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // ---- log ---------------------------------------------------------------

    private void LogSummary(ReportSummary report, long durationMs)
        => _logger.LogInformation(
            "Reporting summary: workspace={WorkspaceId}, scope={ScopeType}, board={BoardId}, period={Days}d, "
            + "boards={Boards}, assignees={Assignees}, capped={Capped}, durationMs={DurationMs}.",
            report.WorkspaceId,
            report.Scope.Type,
            report.Scope.BoardId,
            report.Period.Days,
            report.ByBoard.Count,
            report.ByAssignee.Count,
            report.Truncated.RowCapReached,
            durationMs);
}
