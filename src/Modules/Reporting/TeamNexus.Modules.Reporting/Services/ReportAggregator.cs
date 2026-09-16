using TeamNexus.Modules.Reporting.Contracts;

namespace TeamNexus.Modules.Reporting.Services;

/// <summary>
/// Tầng tính toán của Giai đoạn 6 (Phase 6 §1.2): biến <see cref="ReportWorkspaceSnapshot"/> đã chiếu sẵn
/// thành <see cref="ReportSummary"/>.
/// <para>
/// <b>Hàm THUẦN tuyệt đối</b> — cùng pattern <c>ObserverSignalDetector.Analyze</c> (Phase 5 §3):
/// không <c>DateTime.Now</c> (dùng <c>snapshot.Now</c>), không <c>DbContext</c>, không <c>HttpClient</c>,
/// không <c>Guid.NewGuid</c>, không random, không I/O. Cùng input ⇒ output giống hệt (tất định), nhờ vậy
/// nhóm verify "B. Aggregation thuần" chạy được không cần DB/HTTP và Giai đoạn 8 có thể chuyển thẳng
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

    /// <summary>Múi giờ người xem nhỏ nhất/nhỉ nhất chấp nhận (UTC−14 .. UTC+14), tính bằng phút.</summary>
    private const int MinTzOffsetMinutes = -14 * 60;

    private const int MaxTzOffsetMinutes = 14 * 60;

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

    // ---- chuỗi thời gian cho Burndown/Velocity (Phase 13 §2) ---------------

    /// <summary>
    /// Dựng chuỗi thời gian <c>openTasks</c>/<c>completions</c>/<c>creations</c> từ snapshot đã chiếu sẵn
    /// (Phase 13 §2.2).
    /// <para>
    /// <b>Hàm THUẦN</b> đúng như <see cref="Build"/>: chỉ đọc <paramref name="snapshot"/> (kể cả
    /// <c>Now</c>), không <c>DateTime.Now</c>, không <c>DbContext</c>, không random ⇒ test biên thời gian
    /// chạy được không cần DB/HTTP.
    /// </para>
    /// <para>
    /// Ngày/tuần là ngày **theo múi giờ người xem**: <c>value.AddMinutes(tzOffsetMinutes).UtcDateTime</c>
    /// rồi lấy phần <see cref="DateOnly"/>. Vì vậy một task hoàn thành lúc <c>18:00Z</c> nằm ở ngày tiếp
    /// theo với <c>tzOffsetMinutes = 420</c> (UTC+7) — đúng thứ người dùng thấy trên lịch của họ.
    /// </para>
    /// <para>
    /// Task <b>không</b> đóng được (đang ở cột <c>is_done</c> nhưng thiếu <c>completed_at</c>) bị loại khỏi
    /// <c>openTasks</c> **ngay từ ngày tạo**: dữ liệu cũ không có ngày đóng, nên bịa một ngày là nói sai
    /// với người đọc báo cáo. Điều này khớp <c>BuildProgress</c> (task đó vẫn được tính là <c>done</c>).
    /// </para>
    /// </summary>
    public static ReportProgressSeries BuildProgressSeries(
        ReportWorkspaceSnapshot snapshot,
        ReportThresholds thresholds,
        int? tzOffsetMinutes = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(thresholds);

        var boards = snapshot.Boards ?? [];
        var tasks = FilterToKnownBoards(snapshot.Tasks ?? [], boards);

        var offset = Math.Clamp(tzOffsetMinutes ?? 0, MinTzOffsetMinutes, MaxTzOffsetMinutes);
        var maxBuckets = Math.Max(1, thresholds.MaxSeriesBuckets);

        // Cửa sổ lấy từ snapshot.Range (một nguồn duy nhất với /summary) — KHÔNG dùng snapshot.Now để
        // dựng ngày, vì `Now` là mốc "thời điểm lập báo cáo" trong khi Range.To mới là biên của kỳ.
        var start = LocalDay(snapshot.Range.From, offset);
        var endDay = LocalDay(snapshot.Range.To, offset);

        // Cửa sổ ngắn hơn 1 ngày vẫn phải có ít nhất 1 bucket: nếu không, biểu đồ trống một cách vô cớ.
        if (endDay < start)
        {
            endDay = start;
        }

        // Số bucket theo NGÀY = inclusive (cả ngày đầu và ngày cuối) — vì ngày cuối vẫn là một cột trên
        // biểu đồ. Ngưỡng chuyển sang tuần cũng phải đọc theo cùng con số này, nếu không một cửa sổ
        // "60 ngày" sẽ bị gộp tuần sớm đúng một ngày so với ý định của người gọi.
        var dayBuckets = endDay.DayNumber - start.DayNumber + 1;
        var mode = dayBuckets > Math.Max(1, thresholds.SeriesWeeklyThresholdDays)
            ? ReportSeriesModes.Week
            : ReportSeriesModes.Date;

        // Task đã chiếu sẵn sang ngày local MỘT lần (không phải mỗi ngày mỗi task), để chuỗi N ngày
        // không trở thành O(N × số task) lần cộng thời gian. Kèm luôn cờ "đã xong" theo định nghĩa
        // CHUNG với BuildProgress (cột is_done HOẶC có completed_at).
        var projected = tasks
            .Select(t => new ProjectedTask(
                LocalDay(t.CreatedAt, offset),
                // Task chưa đóng phải mang mốc "KHÔNG BAO GIỜ", không phải `default(DateOnly)`:
                // 0001-01-01 không hề lớn hơn mọi ngày, nên nếu dùng default thì mọi task chưa xong sẽ bị
                // đếm là "hoàn thành" ở mỗi ngày của cửa sổ.
                t.CompletedAt is { } completedAt ? LocalDay(completedAt, offset) : DateOnly.MaxValue,
                t.CompletedAt.HasValue,
                t.IsDoneColumn))
            .ToList();

        var capped = false;

        // Mirror of the per-day series (dài tối đa `dayBuckets`) dùng cho CẢ hai mode: gộp tuần chỉ là
        // phép cộng/lấy phần tử cuối trên chính mảng này, nên hai mode không thể lệch số liệu.
        var days = new List<ReportDailyProgress>(dayBuckets);
        var completions = 0;

        for (var day = start; day <= endDay; day = day.AddDays(1))
        {
            var open = 0;
            var completed = 0;
            var created = 0;

            foreach (var task in projected)
            {
                if (task.CreatedDay == day)
                {
                    created++;
                }

                // Còn mở ở cuối ngày `day`:
                //  • có completed_at ⇒ mở khi đã tạo tới ngày đó VÀ còn đóng SAU ngày đó;
                //  • không có completed_at nhưng đang ở cột is_done (dữ liệu cũ) ⇒ KHÔNG bao giờ mở: không
                //    có ngày đóng nào đáng tin để trừ ra, nên tính nó là mở thì biểu đồ vĩnh viễn cao hơn
                //    thực tế. Khớp `BuildProgress` (task đó vẫn được tính là `done`);
                //  • còn lại ⇒ mở khi đã tạo tới ngày đó.
                var stillOpen = task.HasCompletedAt
                    ? task.CreatedDay <= day && task.CompletedDay > day
                    : task.CreatedDay <= day && !task.IsDoneColumn;

                if (stillOpen)
                {
                    open++;
                }

                if (task.CompletedDay == day)
                {
                    completed++;
                }
            }

            days.Add(new ReportDailyProgress(day, open, completed, created));
            completions += completed;
        }

        IReadOnlyList<ReportDailyProgress> daily;
        IReadOnlyList<ReportWeeklyProgress> weekly;
        var bucketDays = ReportSeriesModes.BucketDays(mode);

        if (mode == ReportSeriesModes.Week)
        {
            daily = [];
            weekly = BuildWeeklyBuckets(days);
        }
        else
        {
            if (days.Count > maxBuckets)
            {
                days = days.TakeLast(maxBuckets).ToList();
                capped = true;
            }

            daily = days;
            weekly = [];
        }

        var openAtEnd = daily.Count > 0
            ? daily[^1].OpenTasks
            : weekly.Count > 0
                ? weekly[^1].OpenAtEnd
                : 0;

        var weeks = Math.Max(1, snapshot.Range.Days / 7.0);

        var velocity = new ReportVelocity(
            AvgCompletionsPerWeek: Round(completions / weeks),
            CompletedInRange: completions,
            OpenAtEnd: openAtEnd);

        return new ReportProgressSeries(
            snapshot.WorkspaceId,
            // `boards` đã được loader lọc theo `?boardId=` (đúng 1 board), nên đây là cách nhận biết scope
            // giống hệt BuildScope — không cần loader truyền scope xuống.
            boards.Count == 1 ? boards[0].BoardId : null,
            snapshot.Range,
            mode,
            daily,
            weekly,
            velocity,
            bucketDays,
            capped,
            maxBuckets,
            offset,
            ProgressSeriesMetricDefinitions());
    }

    /// <summary>Gộp chuỗi ngày thành bucket tuần với mốc là **Thứ Hai** (Phase 13 §2).</summary>
    private static IReadOnlyList<ReportWeeklyProgress> BuildWeeklyBuckets(List<ReportDailyProgress> days)
    {
        var buckets = new List<ReportWeeklyProgress>();
        var index = 0;

        while (index < days.Count)
        {
            var weekStart = StartOfWeek(days[index].Date);
            var completions = 0;
            var creations = 0;
            var openAtEnd = 0;

            // Mọi ngày cùng một tuần nằm liền nhau vì chuỗi được duyệt tăng dần.
            while (index < days.Count && StartOfWeek(days[index].Date) == weekStart)
            {
                completions += days[index].Completions;
                creations += days[index].Creations;
                openAtEnd = days[index].OpenTasks;
                index++;
            }

            buckets.Add(new ReportWeeklyProgress(weekStart, completions, creations, openAtEnd));
        }

        return buckets;
    }

    /// <summary>Thứ Hai của tuần chứa <paramref name="day"/> (<see cref="DayOfWeek.Monday"/> = 1).</summary>
    private static DateOnly StartOfWeek(DateOnly day)
    {
        var offset = ((int)day.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return day.AddDays(-offset);
    }

    /// <summary>
    /// Ngày **theo múi giờ người xem** của một mốc thời gian. Một chỗ duy nhất đổi UTC → ngày local, nên
    /// <c>creations</c>, <c>completions</c> và <c>openTasks</c> không thể lệch múi giờ nhau.
    /// </summary>
    private static DateOnly LocalDay(DateTimeOffset value, int tzOffsetMinutes)
        => DateOnly.FromDateTime(value.AddMinutes(tzOffsetMinutes).UtcDateTime);

    /// <summary>Task đã chiếu sẵn sang ngày local — tránh cộng thời gian lại cho mỗi ngày.</summary>
    /// <param name="CompletedDay">
    /// Ngày đóng theo local; <see cref="DateOnly.MaxValue"/> khi task chưa đóng — dùng
    /// <see cref="DateOnly.MaxValue"/> (không phải <c>default</c>) để "chưa đóng" không bao giờ bằng hoặc
    /// nhỏ hơn một ngày trong cửa sổ.
    /// </param>
    /// <param name="HasCompletedAt">Có <c>completed_at</c> hay không — quyết định nhánh tính "còn mở".</param>
    /// <param name="IsDoneColumn">Task đang ở cột <c>is_done</c> (vế còn lại của <see cref="IsDone"/>).</param>
    private sealed record ProjectedTask(
        DateOnly CreatedDay,
        DateOnly CompletedDay,
        bool HasCompletedAt,
        bool IsDoneColumn);

    /// <summary>
    /// Mô tả từng chỉ số của chuỗi bằng tiếng Việt (Phase 13 §2) — nguồn duy nhất để UI giải thích biểu đồ
    /// mà không cần tài liệu metric thứ hai (cùng nguyên tắc <see cref="MetricDefinitions"/>).
    /// </summary>
    public static IReadOnlyDictionary<string, string> ProgressSeriesMetricDefinitions()
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["openTasks"] = "Số thẻ còn mở ở cuối mỗi mốc thời gian (chưa hoàn thành tính đến hết mốc đó).",
            ["completions"] = "Số thẻ hoàn thành trong mốc, theo completed_at (múi giờ đã chọn).",
            ["creations"] = "Số thẻ được tạo trong mốc, theo created_at (múi giờ đã chọn).",
            ["avgCompletionsPerWeek"] = "Năng suất trung bình = tổng thẻ hoàn thành trong kỳ / (số ngày của kỳ / 7).",
            ["openAtEnd"] = "Số thẻ còn mở ở cuối kỳ báo cáo.",
            ["bucketDays"] = "Độ dài mỗi mốc: 1 = theo ngày, 7 = theo tuần (mốc là Thứ Hai).",
            ["idealOpenSeries"] = "Đường lý tưởng = openTasks − completions; thẻ đóng ở cột is_done nhưng thiếu completed_at bị loại khỏi openTasks từ ngày tạo.",
        };

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
