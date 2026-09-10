using TeamNexus.Modules.Reporting.Contracts;

namespace TeamNexus.Modules.Reporting.Services;

/// <summary>
/// Tầng tính toán của Giai đoạn 6 (Phase 6 §1.2): biến <see cref="ReportWorkspaceSnapshot"/> đã chiếu sẵn
/// thành <see cref="ReportSummary"/>.
/// <para>
/// <b>Hàm THUẦN tuyệt đối</b> — cùng pattern <c>ObserverSignalDetector.Analyze</c> (Phase 5 §3):
/// không <c>DateTime.Now</c> (dùng <c>snapshot.Now</c>), không <c>DbContext</c>, không <c>HttpClient</c>,
/// không <c>Guid.NewGuid</c>, không random, không I/O. Cùng input ⇒ output giống hệt (tất định), nhờ vậy
/// nhóm verify "B. Aggregation thuần" chạy được không cần DB/HTTP và Giai đoạn 7 có thể chuyển thẳng
/// thành test xUnit.
/// </para>
/// <para>
/// Mọi bảng đều được sort tường minh (không phụ thuộc thứ tự nguồn) và mọi tỉ lệ/thời lượng làm tròn
/// 1 chữ số thập phân (<see cref="MidpointRounding.AwayFromZero"/>).
/// </para>
/// </summary>
public static class ReportAggregator
{
    /// <summary>Số chữ số thập phân cho mọi giá trị <c>double</c> trả ra.</summary>
    private const int Decimals = 1;

    /// <summary>
    /// Dựng báo cáo từ snapshot. Không bao giờ ném vì dữ liệu thiếu: danh sách <c>null</c> được coi như rỗng,
    /// task có <c>BoardId</c> lạ (không nằm trong <see cref="ReportWorkspaceSnapshot.Boards"/>) bị bỏ qua.
    /// </summary>
    public static ReportSummary Build(ReportWorkspaceSnapshot snapshot, ReportThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(thresholds);

        var boards = snapshot.Boards ?? [];
        var tasks = FilterToKnownBoards(snapshot.Tasks ?? [], boards);

        var progress = BuildProgress(tasks, snapshot.Now, thresholds);
        var performance = BuildPerformance(tasks, snapshot.Range);

        var byBoard = BuildBoardRows(boards, tasks, snapshot.Range, thresholds.MaxExportRows);
        var byAssignee = BuildAssigneeRows(tasks, snapshot.Range, thresholds.MaxExportRows);

        var activity = BuildActivity(snapshot.Activity, snapshot.Range, thresholds.MaxActionsPerReport);
        var health = BuildHealth(snapshot.Findings, snapshot.RunsScanned);

        var truncation = new ReportTruncation(
            RowCapReached: byBoard.Capped || byAssignee.Capped || activity.Capped,
            MaxRows: thresholds.MaxExportRows);

        return new ReportSummary(
            snapshot.WorkspaceId,
            snapshot.WorkspaceName,
            BuildScope(boards),
            snapshot.Range,
            snapshot.Now,
            progress,
            performance,
            byBoard.Rows,
            byAssignee.Rows,
            activity.Rows,
            health,
            MetricDefinitions(),
            truncation);
    }

    // ---- khối tiến độ (hiện tại) -------------------------------------------

    /// <summary>
    /// Tiến độ hiện tại (Phase 6 §1.3): <c>done</c> = task có <c>completed_at</c> **hoặc** đang ở cột
    /// <c>is_done</c>; <c>overdue</c> = chỉ task **chưa** done (theo cấu hình
    /// <see cref="ReportThresholds.ExcludeDoneOverdue"/>) có <c>due_date &lt; Now</c>
    /// (<c>due_date == Now</c> ⇒ chưa quá hạn, khớp <c>ObserverSignalDetector</c>).
    /// </summary>
    private static ReportProgress BuildProgress(
        IReadOnlyList<ReportTaskSnapshot> tasks,
        DateTimeOffset now,
        ReportThresholds thresholds)
    {
        var total = tasks.Count;
        var done = 0;
        var overdue = 0;

        foreach (var task in tasks)
        {
            var taskIsDone = IsDone(task);

            if (taskIsDone)
            {
                done++;
            }

            // Mặc định (ExcludeDoneOverdue = true) task đã xong không bao giờ bị coi là quá hạn — khớp
            // ObserverSignalDetector. Khi tắt cờ này, task xong nhưng trễ hạn cũng được đếm.
            if ((!taskIsDone || !thresholds.ExcludeDoneOverdue) && IsOverdue(task, now))
            {
                overdue++;
            }
        }

        return new ReportProgress(total, done, total - done, overdue, Percent(done, total));
    }

    // ---- khối hiệu suất (trong cửa sổ) -------------------------------------

    /// <summary>
    /// Hiệu suất trong cửa sổ (Phase 6 §1.3). Task hoàn thành **trước** cửa sổ vẫn nằm trong
    /// <c>Progress.Done</c> nhưng không được tính lại ở đây; các giá trị trung bình trả <c>null</c> khi
    /// mẫu số bằng 0 (≠ 0.0 — UI hiển thị "—").
    /// </summary>
    private static ReportPerformance BuildPerformance(IReadOnlyList<ReportTaskSnapshot> tasks, ReportRange range)
    {
        var completedInRange = tasks.Where(t => InRange(t.CompletedAt, range)).ToList();

        var durations = completedInRange
            .Where(t => t.CompletedAt.HasValue)
            .Select(t => (t.CompletedAt!.Value - t.CreatedAt).TotalHours)
            .ToList();

        var leadDurations = completedInRange
            .Where(t => t.CompletedAt.HasValue && t.DueDate.HasValue)
            .Select(t => (t.CompletedAt!.Value - t.CreatedAt).TotalHours)
            .ToList();

        var doneWithDue = completedInRange
            .Where(t => t.CompletedAt.HasValue && t.DueDate.HasValue)
            .ToList();

        var completedAtMissing = tasks.Count(t => IsDone(t) && !t.CompletedAt.HasValue);

        var weeks = range.Days / 7.0;

        return new ReportPerformance(
            CreatedInRange: tasks.Count(t => InRange(t.CreatedAt, range)),
            CompletedInRange: completedInRange.Count,
            AvgCompletionHours: Average(durations),
            AvgLeadTimeHours: Average(leadDurations),
            OnTimeRate: doneWithDue.Count == 0
                ? null
                : Percent(doneWithDue.Count(t => t.CompletedAt!.Value <= t.DueDate!.Value), doneWithDue.Count),
            OverdueRate: Percent(
                tasks.Count(t => !IsDone(t) && IsOverdue(t, range.To)),
                tasks.Count(t => !IsDone(t))),
            ThroughputPerWeek: weeks <= 0 ? 0.0 : Round(completedInRange.Count / weeks),
            CompletedAtMissing: completedAtMissing);
    }

    // ---- bảng theo bảng / theo người ---------------------------------------

    private static CappedRows<ReportBoardRow> BuildBoardRows(
        IReadOnlyList<ReportBoardSnapshot> boards,
        IReadOnlyList<ReportTaskSnapshot> tasks,
        ReportRange range,
        int maxRows)
    {
        // Mọi board đều có dòng (kể cả board 0 task) để báo cáo không "mất" board khỏi tổng quan.
        var rows = boards
            .Select(board => (board, metrics: BuildRowMetrics(tasks.Where(t => t.BoardId == board.BoardId), range)))
            .Select(x => new ReportBoardRow(
                x.board.BoardId,
                x.board.Name,
                x.metrics.Total,
                x.metrics.Done,
                x.metrics.Open,
                x.metrics.Overdue,
                x.metrics.CompletedInRange,
                x.metrics.AvgCompletionHours,
                x.metrics.OnTimeRate))
            .OrderByDescending(r => r.Total)
            .ThenByDescending(r => r.Open)
            .ThenByDescending(r => r.Overdue)
            .ThenBy(r => r.BoardName, StringComparer.Ordinal)
            .ThenBy(r => r.BoardId.ToString("D"), StringComparer.Ordinal)
            .ToList();

        return Cap(rows, maxRows);
    }

    private static CappedRows<ReportAssigneeRow> BuildAssigneeRows(
        IReadOnlyList<ReportTaskSnapshot> tasks,
        ReportRange range,
        int maxRows)
    {
        var rows = tasks
            .GroupBy(t => t.AssigneeId)
            .Select(group =>
            {
                var metrics = BuildRowMetrics(group, range);
                var name = group.Select(t => t.AssigneeName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

                return new ReportAssigneeRow(
                    group.Key,
                    name ?? ReportUnassigned.Name,
                    metrics.Total,
                    metrics.Done,
                    metrics.Open,
                    metrics.Overdue,
                    metrics.CompletedInRange,
                    metrics.AvgCompletionHours,
                    metrics.OnTimeRate);
            })
            .OrderByDescending(r => r.Total)
            .ThenByDescending(r => r.Open)
            .ThenByDescending(r => r.Overdue)
            .ThenBy(r => r.AssigneeName, StringComparer.Ordinal)
            .ThenBy(r => r.AssigneeId?.ToString("D") ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        return Cap(rows, maxRows);
    }

    /// <summary>Bộ chỉ số con dùng chung cho một dòng "theo bảng"/"theo người" (Phase 6 §1.3).</summary>
    private static RowMetrics BuildRowMetrics(IEnumerable<ReportTaskSnapshot> tasks, ReportRange range)
    {
        var list = tasks.ToList();

        var done = list.Count(IsDone);
        var completedInRange = list.Where(t => InRange(t.CompletedAt, range)).ToList();
        var durations = completedInRange
            .Where(t => t.CompletedAt.HasValue)
            .Select(t => (t.CompletedAt!.Value - t.CreatedAt).TotalHours)
            .ToList();
        var withDue = completedInRange.Where(t => t.CompletedAt.HasValue && t.DueDate.HasValue).ToList();

        return new RowMetrics(
            Total: list.Count,
            Done: done,
            Open: list.Count - done,
            Overdue: list.Count(t => !IsDone(t) && IsOverdue(t, range.To)),
            CompletedInRange: completedInRange.Count,
            AvgCompletionHours: Average(durations),
            OnTimeRate: withDue.Count == 0
                ? null
                : Percent(withDue.Count(t => t.CompletedAt!.Value <= t.DueDate!.Value), withDue.Count));
    }

    // ---- khối hoạt động ----------------------------------------------------

    private static CappedActivityRows BuildActivity(
        ReportActivitySnapshot? activity,
        ReportRange range,
        int maxActions)
    {
        var byAction = (activity?.ByAction ?? [])
            .GroupBy(a => a.Action)
            .Select(g => new ReportActionRow(g.Key, g.Sum(a => a.Count)))
            .OrderByDescending(a => a.Count)
            .ThenBy(a => a.Action, StringComparer.Ordinal)
            .ToList();

        var capped = Cap(byAction, maxActions);
        var totalActions = activity?.TotalActions ?? byAction.Sum(a => a.Count);
        var days = range.Days;

        var result = new ReportActivity(
            totalActions,
            capped.Rows,
            activity?.ActiveUsers ?? 0,
            days <= 0 ? 0.0 : Round(totalActions / (double)days));

        return new CappedActivityRows(result, capped.Capped);
    }

    // ---- khối sức khoẻ dự án ----------------------------------------------

    /// <summary>
    /// Tổng hợp kết quả Observer trong cửa sổ (Phase 6 §1.3). Luôn được tính (kể cả khi rỗng) — việc có
    /// hiển thị khối này hay không là quyết định của renderer qua <c>Reports:IncludeHealthSection</c>.
    /// </summary>
    private static ReportHealth BuildHealth(IReadOnlyList<ReportFindingCount>? findings, int runsScanned)
    {
        var signalsByType = (findings ?? [])
            .GroupBy(f => string.IsNullOrWhiteSpace(f.Type) ? ReportSeverities.Unknown : f.Type)
            .Select(g => new ReportSignalTypeRow(g.Key, g.Sum(f => f.Count)))
            .OrderByDescending(s => s.Count)
            .ThenBy(s => s.Type, StringComparer.Ordinal)
            .ToList();

        var findingsBySeverity = (findings ?? [])
            .GroupBy(f => ReportSeverities.Normalize(f.Severity))
            .Select(g => new ReportSeverityRow(g.Key, g.Sum(f => f.Count)))
            .OrderByDescending(s => s.Count)
            .ThenBy(s => s.Severity, StringComparer.Ordinal)
            .ToList();

        return new ReportHealth(runsScanned, signalsByType, findingsBySeverity);
    }

    // ---- scope & cap -------------------------------------------------------

    /// <summary>
    /// Scope suy ra từ số board trong snapshot: loader (§3) đã lọc snapshot theo <c>?boardId=</c>, nên
    /// đúng 1 board nghĩa là báo cáo theo board. Aggregator **không** lọc lại board.
    /// </summary>
    private static ReportScope BuildScope(IReadOnlyList<ReportBoardSnapshot> boards)
        => boards.Count == 1
            ? new ReportScope(ReportScopeTypes.Board, boards[0].BoardId, boards[0].Name)
            : new ReportScope(ReportScopeTypes.Workspace, null, null);

    /// <summary>Bỏ task không thuộc board nào trong scope (phòng thủ khi loader chiếu thừa dữ liệu).</summary>
    private static IReadOnlyList<ReportTaskSnapshot> FilterToKnownBoards(
        IReadOnlyList<ReportTaskSnapshot> tasks,
        IReadOnlyList<ReportBoardSnapshot> boards)
    {
        if (tasks.Count == 0)
        {
            return tasks;
        }

        var known = boards.Select(b => b.BoardId).ToHashSet();
        var filtered = tasks.Where(t => known.Contains(t.BoardId)).ToList();

        return filtered.Count == tasks.Count ? tasks : filtered;
    }

    /// <summary>Cắt danh sách theo cap và cho biết đã cắt hay chưa (Phase 6 §0 D13).</summary>
    private static CappedRows<T> Cap<T>(IReadOnlyList<T> rows, int maxRows)
    {
        var cap = Math.Max(1, maxRows);

        return rows.Count <= cap
            ? new CappedRows<T>(rows, false)
            : new CappedRows<T>(rows.Take(cap).ToList(), true);
    }

    // ---- helper số học & metric definitions --------------------------------

    private static bool IsDone(ReportTaskSnapshot task) => task.IsDoneColumn || task.CompletedAt.HasValue;

    private static bool IsOverdue(ReportTaskSnapshot task, DateTimeOffset now)
        => task.DueDate.HasValue && task.DueDate.Value < now;

    private static bool InRange(DateTimeOffset? value, ReportRange range)
        => value.HasValue && value.Value >= range.From && value.Value <= range.To;

    /// <summary>Tỉ lệ % làm tròn 1 chữ số; mẫu số 0 ⇒ 0.0 (không bao giờ NaN/Infinity).</summary>
    private static double Percent(int numerator, int denominator)
        => denominator <= 0 ? 0.0 : Round(numerator * 100.0 / denominator);

    private static double? Average(IReadOnlyList<double> values)
        => values.Count == 0 ? null : Round(values.Average());

    private static double Round(double value)
        => Math.Round(double.IsFinite(value) ? value : 0.0, Decimals, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Mô tả từng metric bằng tiếng Việt (Phase 6 §1.3) — **nguồn duy nhất** cho cả 3 bề mặt: response
    /// JSON, bảng "Hiệu suất" trong PDF và sheet Excel, nên báo cáo tự giải thích mà không cần tài liệu
    /// metric thứ hai. Thứ tự key cố định theo bảng công thức.
    /// </summary>
    public static IReadOnlyDictionary<string, string> MetricDefinitions()
    {
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["total"] = "Số task hiện có trong phạm vi báo cáo (không tính task đã xoá mềm).",
            ["done"] = "Task có completed_at, hoặc đang nằm ở cột được đánh dấu is_done.",
            ["open"] = "Task chưa hoàn thành = total − done.",
            ["overdue"] = "Task chưa xong có due_date < thời điểm lập báo cáo (đúng hạn vào lúc bằng nhau ⇒ chưa quá hạn).",
            ["donePercent"] = "Tỉ lệ hoàn thành hiện tại = done / total × 100.",
            ["createdInRange"] = "Số task được tạo trong khoảng thời gian báo cáo.",
            ["completedInRange"] = "Số task hoàn thành trong khoảng thời gian báo cáo (task xong trước đó vẫn được tính ở 'done').",
            ["avgCompletionHours"] = "Thời gian hoàn thành trung bình (giờ) = trung bình (completed_at − created_at) của task hoàn thành trong khoảng; '—' khi chưa có mẫu.",
            ["avgLeadTimeHours"] = "Như trên nhưng chỉ tính các task có due_date, để thấy ảnh hưởng của hạn chót.",
            ["onTimeRate"] = "Tỉ lệ hoàn thành đúng hạn = % task có completed_at ≤ due_date; task không có due_date bị loại khỏi mẫu.",
            ["overdueRate"] = "Tỉ lệ quá hạn trên số task đang mở = overdue / open × 100.",
            ["throughputPerWeek"] = "Năng suất tuần = completedInRange / (số ngày của khoảng / 7).",
            ["completedAtMissing"] = "Task đang ở cột done nhưng thiếu completed_at (dữ liệu cũ) — không được tính vào mọi chỉ số thời gian.",
            ["byBoard"] = "Cùng bộ chỉ số, chia theo từng bảng Kanban.",
            ["byAssignee"] = "Cùng bộ chỉ số, chia theo người phụ trách; task không có người phụ trách gom thành 'Chưa gán'.",
            ["totalActions"] = "Tổng số sự kiện trong activity_logs thuộc khoảng thời gian báo cáo.",
            ["byAction"] = "Số sự kiện theo từng loại hành động (TaskCreated, TaskMoved, CommentAdded, …).",
            ["activeUsers"] = "Số người dùng khác nhau đã tạo sự kiện trong khoảng thời gian báo cáo.",
            ["actionsPerDay"] = "Số sự kiện trung bình mỗi ngày trong khoảng thời gian báo cáo.",
            ["runsScanned"] = "Số lần AI Observer quét xong (status = Completed) trong khoảng thời gian báo cáo.",
            ["signalsByType"] = "Số tín hiệu Observer phát hiện theo loại (OverdueTask, StalledTask, Overload, Bottleneck).",
            ["findingsBySeverity"] = "Số cảnh báo Observer đã ghi theo mức độ (Low/Medium/High/Critical; giá trị lạ gom thành 'Unknown').",
        };

        return definitions;
    }

    // ---- kiểu nội bộ -------------------------------------------------------

    private sealed record RowMetrics(
        int Total,
        int Done,
        int Open,
        int Overdue,
        int CompletedInRange,
        double? AvgCompletionHours,
        double? OnTimeRate);

    private sealed record CappedRows<T>(IReadOnlyList<T> Rows, bool Capped);

    private sealed record CappedActivityRows(ReportActivity Rows, bool Capped);
}
