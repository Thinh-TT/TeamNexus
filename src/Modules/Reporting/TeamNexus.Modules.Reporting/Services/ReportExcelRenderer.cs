using System.Globalization;
using ClosedXML.Excel;
using TeamNexus.Modules.Reporting.Contracts;
using TeamNexus.Modules.Reporting.Options;

namespace TeamNexus.Modules.Reporting.Services;

/// <summary>
/// Render Excel bằng ClosedXML (Phase 6 §4.4) — file **trong RAM**, không lưu disk.
/// <para>
/// 5 sheet tên cố định: <c>Tổng quan</c>, <c>Theo bảng</c>, <c>Theo người</c>, <c>Hoạt động</c>,
/// <c>Sức khoẻ AI</c>. Mỗi sheet có tiêu đề + kỳ báo cáo ở hàng đầu, header bảng in đậm, auto-filter,
/// freeze pane và <b>data bar</b> cho tỉ lệ % (thay cho chart — ClosedXML không hỗ trợ chart native,
/// Phase 6 §0 D15). Mọi giá trị là **literal** từ aggregator: không công thức ⇒ không thể sinh `#REF!`.
/// </para>
/// <para>
/// Format số/ngày dùng **literal format code** (<c>0.0</c>, <c>0.0"%"</c>, <c>0</c>, chuỗi ngày tự format)
/// nên không phụ thuộc culture của máy chạy.
/// </para>
/// </summary>
public sealed class ReportExcelRenderer : IReportExcelRenderer
{
    private const string Dash = "—";
    private const string NumberFormatOneDecimal = "0.0";
    private const string NumberFormatPercent = "0.0\"%\"";
    private const string NumberFormatInteger = "0";
    private const double MaxColumnWidth = 60;

    public byte[] Render(ReportSummary report, ReportsOptions options)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(options);

        using var workbook = new XLWorkbook();

        ComposeOverview(workbook, report, options);
        ComposeBoardSheet(workbook, report, options);
        ComposeAssigneeSheet(workbook, report, options);
        ComposeActivitySheet(workbook, report, options);

        if (options.IncludeHealthSection)
        {
            ComposeHealthSheet(workbook, report, options);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return stream.ToArray();
    }

    // ---- sheet 1: Tổng quan ------------------------------------------------

    private static void ComposeOverview(XLWorkbook workbook, ReportSummary report, ReportsOptions options)
    {
        var sheet = workbook.Worksheets.Add("Tổng quan");
        var row = WriteSheetTitle(sheet, report, options, "BÁO CÁO TIẾN ĐỘ & HIỆU SUẤT");

        // Local function: ghi 1 dòng metric (nhãn | giá trị | định nghĩa). Closure qua
        // report.MetricDefinitions ⇒ không cần state tĩnh, và null ⇒ "—" (không in 0).
        int WriteMetric(int currentRow, string label, double? value, string numberFormat, string? definitionKey)
        {
            sheet.Cell(currentRow, 1).Value = label;

            if (value.HasValue)
            {
                sheet.Cell(currentRow, 2).Value = value.Value;
                sheet.Cell(currentRow, 2).Style.NumberFormat.Format = numberFormat;
            }
            else
            {
                sheet.Cell(currentRow, 2).Value = Dash;
            }

            if (definitionKey is not null
                && report.MetricDefinitions.TryGetValue(definitionKey, out var definition)
                && !string.IsNullOrWhiteSpace(definition))
            {
                sheet.Cell(currentRow, 3).Value = definition;
                sheet.Cell(currentRow, 3).Style.Font.SetFontSize(9);
            }

            return currentRow + 1;
        }

        row = WriteSectionTitle(sheet, row, "Tiến độ hiện tại");
        WriteHeaderRow(sheet, row, ["Chỉ số", "Giá trị", "Định nghĩa"]);
        ApplyTableStyle(sheet, row, row, 1, 3);
        row++;
        row = WriteMetric(row, "Tổng số task", report.Progress.Total, NumberFormatInteger, "total");
        row = WriteMetric(row, "Hoàn thành", report.Progress.Done, NumberFormatInteger, "done");
        row = WriteMetric(row, "Đang mở", report.Progress.Open, NumberFormatInteger, "open");
        row = WriteMetric(row, "Quá hạn", report.Progress.Overdue, NumberFormatInteger, "overdue");
        row = WriteMetric(row, "Tỉ lệ hoàn thành (%)", report.Progress.DonePercent, NumberFormatPercent, "donePercent");
        row++;

        row = WriteSectionTitle(sheet, row, "Hiệu suất trong kỳ");
        var performance = report.Performance;
        row = WriteMetric(row, "Task tạo trong kỳ", performance.CreatedInRange, NumberFormatInteger, "createdInRange");
        row = WriteMetric(row, "Task hoàn thành trong kỳ", performance.CompletedInRange, NumberFormatInteger, "completedInRange");
        row = WriteMetric(row, "Thời gian hoàn thành TB (giờ)", performance.AvgCompletionHours, NumberFormatOneDecimal, "avgCompletionHours");
        row = WriteMetric(row, "Lead time có hạn chót TB (giờ)", performance.AvgLeadTimeHours, NumberFormatOneDecimal, "avgLeadTimeHours");
        row = WriteMetric(row, "Tỉ lệ đúng hạn (%)", performance.OnTimeRate, NumberFormatPercent, "onTimeRate");
        row = WriteMetric(row, "Tỉ lệ quá hạn (%)", performance.OverdueRate, NumberFormatPercent, "overdueRate");
        row = WriteMetric(row, "Năng suất tuần", performance.ThroughputPerWeek, NumberFormatOneDecimal, "throughputPerWeek");
        row = WriteMetric(row, "Task done thiếu completed_at", performance.CompletedAtMissing, NumberFormatInteger, "completedAtMissing");
        row++;

        // Định nghĩa metric: báo cáo tự giải thích (cùng nguồn metricDefinitions với PDF/JSON).
        row = WriteSectionTitle(sheet, row, "Định nghĩa chỉ số");
        sheet.Cell(row, 1).Value = "Chỉ số";
        sheet.Cell(row, 2).Value = "Định nghĩa";
        var headerRow = row;
        row++;
        foreach (var (key, definition) in report.MetricDefinitions)
        {
            sheet.Cell(row, 1).Value = key;
            sheet.Cell(row, 2).Value = definition;
            row++;
        }

        if (report.MetricDefinitions.Count > 0)
        {
            ApplyTableStyle(sheet, headerRow, headerRow, 1, 2);
        }

        row++;
        if (report.Truncated.RowCapReached)
        {
            sheet.Cell(row, 1).Value =
                $"⚠ Dữ liệu đã được cắt ở {report.Truncated.MaxRows} dòng — số liệu tổng vẫn đầy đủ, chỉ bảng chi tiết bị giới hạn.";
            sheet.Range(row, 1, row, 2).Merge().Style.Font.SetBold().Font.SetFontColor(XLColor.Orange);
            row++;
        }

        sheet.Cell(row, 1).Value = $"Sinh tự động lúc {FormatUtc(report.GeneratedAt)} UTC · Không lưu trên server";
        sheet.Cell(row + 1, 1).Value = "Nguồn: tasks/boards/board_columns/activity_logs/ai_observer_runs";
        sheet.Range(row, 1, row + 1, 4).Style.Font.SetItalic().Font.SetFontSize(9).Font.SetFontColor(XLColor.Gray);

        FinalizeSheet(sheet, freezeRow: 2, autoFilterRange: null);
    }

    // ---- sheet 2: Theo bảng ------------------------------------------------

    private static void ComposeBoardSheet(XLWorkbook workbook, ReportSummary report, ReportsOptions options)
    {
        var sheet = workbook.Worksheets.Add("Theo bảng");
        var row = WriteSheetTitle(sheet, report, options, "TIẾN ĐỘ THEO BẢNG");

        string[] headers =
        [
            "Bảng", "Tổng", "Done", "Đang mở", "Quá hạn", "Xong trong kỳ", "TB giờ", "Đúng hạn (%)",
        ];

        var headerRow = row;
        WriteHeaderRow(sheet, row, headers);
        row++;

        if (report.ByBoard.Count == 0)
        {
            WriteEmptyRow(sheet, row, headers.Length);
        }
        else
        {
            foreach (var board in report.ByBoard)
            {
                sheet.Cell(row, 1).Value = board.BoardName;
                sheet.Cell(row, 2).Value = board.Total;
                sheet.Cell(row, 3).Value = board.Done;
                sheet.Cell(row, 4).Value = board.Open;
                sheet.Cell(row, 5).Value = board.Overdue;
                sheet.Cell(row, 6).Value = board.CompletedInRange;
                WriteNullableNumber(sheet.Cell(row, 7), board.AvgCompletionHours, NumberFormatOneDecimal);
                WriteNullableNumber(sheet.Cell(row, 8), board.OnTimeRate, NumberFormatPercent);

                // Data bar trực quan cho quy mô task và tỉ lệ đúng hạn (thay chart — §0 D15).
                AddDataBar(sheet.Cell(row, 2), XLColor.LightBlue);
                if (board.OnTimeRate.HasValue)
                {
                    AddDataBar(sheet.Cell(row, 8), XLColor.LightGreen);
                }

                row++;
            }
        }

        ApplyTableStyle(sheet, headerRow, headerRow, 1, headers.Length);
        FinalizeSheet(sheet, freezeRow: headerRow, autoFilterRange: sheet.Range(headerRow, 1, headerRow, headers.Length));
    }

    // ---- sheet 3: Theo người -----------------------------------------------

    private static void ComposeAssigneeSheet(XLWorkbook workbook, ReportSummary report, ReportsOptions options)
    {
        var sheet = workbook.Worksheets.Add("Theo người");
        var row = WriteSheetTitle(sheet, report, options, "HIỆU SUẤT THEO NGƯỜI PHỤ TRÁCH");

        string[] headers =
        [
            "Người phụ trách", "Tổng", "Done", "Đang mở", "Quá hạn", "Xong trong kỳ", "TB giờ", "Đúng hạn (%)",
        ];

        var headerRow = row;
        WriteHeaderRow(sheet, row, headers);
        row++;

        if (report.ByAssignee.Count == 0)
        {
            WriteEmptyRow(sheet, row, headers.Length);
        }
        else
        {
            foreach (var assignee in report.ByAssignee)
            {
                sheet.Cell(row, 1).Value = string.IsNullOrWhiteSpace(assignee.AssigneeName)
                    ? ReportUnassigned.Name
                    : assignee.AssigneeName;
                sheet.Cell(row, 2).Value = assignee.Total;
                sheet.Cell(row, 3).Value = assignee.Done;
                sheet.Cell(row, 4).Value = assignee.Open;
                sheet.Cell(row, 5).Value = assignee.Overdue;
                sheet.Cell(row, 6).Value = assignee.CompletedInRange;
                WriteNullableNumber(sheet.Cell(row, 7), assignee.AvgCompletionHours, NumberFormatOneDecimal);
                WriteNullableNumber(sheet.Cell(row, 8), assignee.OnTimeRate, NumberFormatPercent);

                AddDataBar(sheet.Cell(row, 2), XLColor.LightBlue);
                if (assignee.OnTimeRate.HasValue)
                {
                    AddDataBar(sheet.Cell(row, 8), XLColor.LightGreen);
                }

                row++;
            }
        }

        ApplyTableStyle(sheet, headerRow, headerRow, 1, headers.Length);
        FinalizeSheet(sheet, freezeRow: headerRow, autoFilterRange: sheet.Range(headerRow, 1, headerRow, headers.Length));

        if (report.ByAssignee.Count > 0)
        {
            sheet.Cell(row + 1, 1).Value =
                "Task không có người phụ trách được gom vào nhóm \"Chưa gán\".";
            sheet.Range(row + 1, 1, row + 1, 4).Merge().Style.Font.SetItalic().Font.SetFontSize(9);
        }
    }

    // ---- sheet 4: Hoạt động ------------------------------------------------

    private static void ComposeActivitySheet(XLWorkbook workbook, ReportSummary report, ReportsOptions options)
    {
        var sheet = workbook.Worksheets.Add("Hoạt động");
        var row = WriteSheetTitle(sheet, report, options, "HOẠT ĐỘNG TRONG KỲ");

        var activity = report.Activity;
        sheet.Cell(row, 1).Value = "Tổng sự kiện";
        sheet.Cell(row, 2).Value = activity.TotalActions;
        sheet.Cell(row, 3).Value = "Người dùng hoạt động";
        sheet.Cell(row, 4).Value = activity.ActiveUsers;
        sheet.Cell(row, 5).Value = "Trung bình/ngày";
        sheet.Cell(row, 6).Value = activity.ActionsPerDay;
        sheet.Cell(row, 6).Style.NumberFormat.Format = NumberFormatOneDecimal;
        row += 2;

        string[] headers = ["Hành động", "Mã gốc", "Số lần"];
        var headerRow = row;
        WriteHeaderRow(sheet, row, headers);
        row++;

        if (activity.ByAction.Count == 0)
        {
            WriteEmptyRow(sheet, row, headers.Length, "Không có sự kiện nào trong kỳ báo cáo.");
        }
        else
        {
            foreach (var action in activity.ByAction)
            {
                sheet.Cell(row, 1).Value = ReportLabels.Action(action.Action);
                sheet.Cell(row, 2).Value = action.Action;
                sheet.Cell(row, 3).Value = action.Count;
                AddDataBar(sheet.Cell(row, 3), XLColor.LightBlue);
                row++;
            }
        }

        ApplyTableStyle(sheet, headerRow, headerRow, 1, headers.Length);
        FinalizeSheet(sheet, freezeRow: headerRow, autoFilterRange: sheet.Range(headerRow, 1, headerRow, headers.Length));
    }

    // ---- sheet 5: Sức khoẻ AI ---------------------------------------------

    private static void ComposeHealthSheet(XLWorkbook workbook, ReportSummary report, ReportsOptions options)
    {
        var sheet = workbook.Worksheets.Add("Sức khoẻ AI");
        var row = WriteSheetTitle(sheet, report, options, "SỨC KHOẺ DỰ ÁN (AI OBSERVER)");

        var health = report.Health;
        sheet.Cell(row, 1).Value = "Số lần quét trong kỳ";
        sheet.Cell(row, 2).Value = health.RunsScanned;
        row += 2;

        string[] headers = ["Tín hiệu", "Mã gốc", "Số lượng"];
        var signalHeader = row;
        WriteHeaderRow(sheet, row, headers);
        row++;

        if (health.SignalsByType.Count == 0)
        {
            WriteEmptyRow(sheet, row, headers.Length, "Không có tín hiệu nào trong kỳ báo cáo.");
            row++;
        }
        else
        {
            foreach (var signal in health.SignalsByType)
            {
                sheet.Cell(row, 1).Value = ReportLabels.SignalType(signal.Type);
                sheet.Cell(row, 2).Value = signal.Type;
                sheet.Cell(row, 3).Value = signal.Count;
                AddDataBar(sheet.Cell(row, 3), XLColor.LightBlue);
                row++;
            }
        }

        ApplyTableStyle(sheet, signalHeader, signalHeader, 1, headers.Length);
        row++;

        string[] severityHeaders = ["Mức độ cảnh báo", "Mã gốc", "Số lượng"];
        var severityHeader = row;
        WriteHeaderRow(sheet, row, severityHeaders);
        row++;

        if (health.FindingsBySeverity.Count == 0)
        {
            WriteEmptyRow(sheet, row, severityHeaders.Length, "Không có cảnh báo nào trong kỳ báo cáo.");
        }
        else
        {
            foreach (var severity in health.FindingsBySeverity)
            {
                sheet.Cell(row, 1).Value = ReportLabels.Severity(severity.Severity);
                sheet.Cell(row, 2).Value = severity.Severity;
                sheet.Cell(row, 3).Value = severity.Count;
                AddDataBar(sheet.Cell(row, 3), XLColor.LightBlue);
                row++;
            }
        }

        ApplyTableStyle(sheet, severityHeader, severityHeader, 1, severityHeaders.Length);
        FinalizeSheet(sheet, freezeRow: signalHeader, autoFilterRange: sheet.Range(signalHeader, 1, signalHeader, severityHeaders.Length));
    }

    // ---- helper ghi sheet --------------------------------------------------

    private static int WriteSheetTitle(IXLWorksheet sheet, ReportSummary report, ReportsOptions options, string title)
    {
        sheet.Cell(1, 1).Value = title;
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);

        var scope = report.Scope.Type == ReportScopeTypes.Board && !string.IsNullOrWhiteSpace(report.Scope.BoardName)
            ? $"{report.WorkspaceName} › {report.Scope.BoardName}"
            : report.WorkspaceName;

        sheet.Cell(2, 1).Value = $"{options.CompanyName} · {scope} · {report.Period.Label} · Lập lúc {FormatUtc(report.GeneratedAt)} UTC";
        sheet.Range(2, 1, 2, 4).Merge().Style.Font.SetFontSize(9).Font.SetFontColor(XLColor.Gray);

        return 4;
    }

    private static int WriteSectionTitle(IXLWorksheet sheet, int row, string title)
    {
        sheet.Cell(row, 1).Value = title;
        sheet.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(11);

        return row + 1;
    }

    /// <summary>
    /// Thêm data bar cho một ô. API thật của ClosedXML 0.105 là
    /// <c>cell.AddConditionalFormat().DataBar(color, showValue)</c> — **không** có <c>cell.AddDataBar()</c>
    /// (khác câu chữ trong file task; xem ghi chú điều chỉnh ở §4.4).
    /// </summary>
    private static void AddDataBar(IXLCell cell, XLColor color)
        => cell.AddConditionalFormat().DataBar(color, true);

    private static void WriteHeaderRow(IXLWorksheet sheet, int row, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            sheet.Cell(row, i + 1).Value = headers[i];
        }
    }

    private static void WriteEmptyRow(IXLWorksheet sheet, int row, int columnCount, string message = "Không có dữ liệu trong kỳ báo cáo.")
    {
        sheet.Cell(row, 1).Value = message;
        sheet.Range(row, 1, row, columnCount).Merge().Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);
    }

    private static void WriteNullableNumber(IXLCell cell, double? value, string numberFormat)
    {
        if (value.HasValue)
        {
            cell.Value = value.Value;
            cell.Style.NumberFormat.Format = numberFormat;
        }
        else
        {
            cell.Value = Dash;
        }
    }

    private static void ApplyTableStyle(IXLWorksheet sheet, int headerRow, int lastHeaderRow, int firstColumn, int lastColumn)
    {
        var header = sheet.Range(headerRow, firstColumn, lastHeaderRow, lastColumn);
        header.Style.Font.SetBold();
        header.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8EAF6"));
        header.Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
    }

    private static void FinalizeSheet(IXLWorksheet sheet, int freezeRow, IXLRange? autoFilterRange)
    {
        if (autoFilterRange is not null)
        {
            autoFilterRange.SetAutoFilter();
        }

        if (freezeRow > 1)
        {
            sheet.SheetView.FreezeRows(freezeRow);
        }

        sheet.Columns().AdjustToContents();

        foreach (var column in sheet.ColumnsUsed())
        {
            if (column.Width > MaxColumnWidth)
            {
                column.Width = MaxColumnWidth;
            }
        }
    }

    private static string FormatUtc(DateTimeOffset value)
        => value.UtcDateTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}
