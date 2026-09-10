using System.Globalization;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TeamNexus.Modules.Reporting.Contracts;
using TeamNexus.Modules.Reporting.Options;

namespace TeamNexus.Modules.Reporting.Services;

/// <summary>
/// Render PDF bằng QuestPDF (Phase 6 §4.3) — sinh file **trong RAM**, không lưu disk.
/// <para>
/// Bố cục: trang bìa → tiến độ hiện tại (kèm thanh tiến độ) → hiệu suất trong cửa sổ (kèm cột định nghĩa
/// lấy từ <c>MetricDefinitions</c> nên báo cáo tự giải thích) → theo bảng → theo người → hoạt động →
/// sức khoẻ AI (nếu bật) → ghi chú cuối + cảnh báo cắt dòng; footer mọi trang có tên đơn vị và số trang.
/// </para>
/// <para>
/// Font dùng bộ mặc định của QuestPDF (Lato có sẵn trong package, hỗ trợ tiếng Việt) — **không** trỏ tới
/// font hệ thống, vì máy CI/deploy không có sẵn font đó.
/// </para>
/// </summary>
public sealed class ReportPdfRenderer : IReportPdfRenderer
{
    private const string Dash = "—";

    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    private readonly ILogger<ReportPdfRenderer> _logger;

    public ReportPdfRenderer(ILogger<ReportPdfRenderer> logger)
    {
        _logger = logger;

        // QuestPDF yêu cầu khai báo license tier MỘT LẦN trước khi sinh document. Đặt ngay trong renderer
        // (không đặt ở AddReportingModule) để renderer tự đủ — dùng được từ harness/test/host nào cũng
        // không phụ thuộc thứ tự đăng ký DI. Phase 6 §0 D14: tier Community (miễn phí cho cá nhân/
        // tổ chức < 1M USD doanh thu). Gán lại cùng giá trị là no-op.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(ReportSummary report, ReportsOptions options)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(options);

        var pageSize = ResolvePageSize(options);

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(pageSize);
                page.Margin(28);
                page.DefaultTextStyle(text => text.FontSize(9.5f));

                page.Header().Element(container => ComposeHeader(container, report, options));
                page.Content().Element(container => ComposeContent(container, report, options));
                page.Footer().Element(container => ComposeFooter(container, options));
            });
        }).GeneratePdf();
    }

    // ---- các khối ----------------------------------------------------------

    private static void ComposeHeader(IContainer container, ReportSummary report, ReportsOptions options)
    {
        container.PaddingBottom(10).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("BÁO CÁO TIẾN ĐỘ & HIỆU SUẤT").FontSize(16).Bold().FontColor(Colors.Indigo.Darken2);
                column.Item().Text($"{options.CompanyName} · {ScopeTitle(report)}").FontSize(10).FontColor(Colors.Grey.Darken2);
            });

            row.ConstantItem(190).AlignRight().Column(column =>
            {
                column.Item().Text(report.Period.Label).FontSize(9);
                column.Item().Text($"Lập lúc: {FormatUtc(report.GeneratedAt)} UTC").FontSize(9);
            });
        });
    }

    private static void ComposeContent(IContainer container, ReportSummary report, ReportsOptions options)
    {
        container.PaddingVertical(10).Column(column =>
        {
            column.Spacing(14);

            column.Item().Element(element => ComposeProgress(element, report));
            column.Item().Element(element => ComposePerformance(element, report));
            column.Item().Element(element => ComposeBoardTable(element, report));
            column.Item().Element(element => ComposeAssigneeTable(element, report));
            column.Item().Element(element => ComposeActivity(element, report));

            if (options.IncludeHealthSection)
            {
                column.Item().Element(element => ComposeHealth(element, report));
            }

            column.Item().Element(element => ComposeNotes(element, report));
        });
    }

    private static void ComposeProgress(IContainer container, ReportSummary report)
    {
        var progress = report.Progress;

        container.Column(column =>
        {
            column.Item().Element(element => SectionTitle(element, "1. Tiến độ hiện tại"));
            column.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem(3).Column(stats =>
                {
                    stats.Spacing(3);
                    stats.Item().Text($"Tổng số task: {progress.Total}");
                    stats.Item().Text($"Hoàn thành: {progress.Done}");
                    stats.Item().Text($"Đang mở: {progress.Open}");
                    stats.Item().Text($"Quá hạn: {progress.Overdue}");
                    stats.Item().Text($"Tỉ lệ hoàn thành: {Percent(progress.DonePercent)}");
                });

                // Thanh tiến độ: 2 ô tỉ lệ (100 − %done / %done) ⇒ không cần width theo % phần tử,
                // và tỉ lệ 0% vẫn hợp lệ (ô "đã xong" bị bỏ để không tạo width = 0).
                row.RelativeItem(4).PaddingLeft(10).AlignMiddle().Element(progressBar =>
                {
                    var ratio = (float)Math.Clamp(progress.DonePercent / 100.0, 0.0, 1.0);

                    progressBar.Height(14).Background(Colors.Grey.Lighten3).Row(bar =>
                    {
                        if (ratio > 0)
                        {
                            bar.RelativeItem(ratio).Background(Colors.Green.Medium);
                        }

                        if (ratio < 1)
                        {
                            bar.RelativeItem(1 - ratio);
                        }
                    });
                });
            });
        });
    }

    private static void ComposePerformance(IContainer container, ReportSummary report)
    {
        var performance = report.Performance;

        var rows = new List<(string Metric, string Value, string Definition)>
        {
            ("Task tạo trong kỳ", Int(performance.CreatedInRange), Definition(report, "createdInRange")),
            ("Task hoàn thành trong kỳ", Int(performance.CompletedInRange), Definition(report, "completedInRange")),
            ("Thời gian hoàn thành TB (giờ)", Hours(performance.AvgCompletionHours), Definition(report, "avgCompletionHours")),
            ("Lead time có hạn chót TB (giờ)", Hours(performance.AvgLeadTimeHours), Definition(report, "avgLeadTimeHours")),
            ("Tỉ lệ đúng hạn", PercentOrDash(performance.OnTimeRate), Definition(report, "onTimeRate")),
            ("Tỉ lệ quá hạn", Percent(performance.OverdueRate), Definition(report, "overdueRate")),
            ("Năng suất tuần", Number(performance.ThroughputPerWeek), Definition(report, "throughputPerWeek")),
            ("Task done thiếu completed_at", Int(performance.CompletedAtMissing), Definition(report, "completedAtMissing")),
        };

        container.Column(column =>
        {
            column.Item().Element(element => SectionTitle(element, "2. Hiệu suất trong kỳ"));
            column.Item().PaddingTop(6).Element(element => Table(element,
                ["Chỉ số", "Giá trị", "Định nghĩa"],
                rows.Select(r => new[] { r.Metric, r.Value, r.Definition }).ToList(),
                widths: [3f, 1.2f, 6f]));
        });
    }

    private static void ComposeBoardTable(IContainer container, ReportSummary report)
    {
        var rows = report.ByBoard
            .Select(row => new[]
            {
                row.BoardName,
                Int(row.Total),
                Int(row.Done),
                Int(row.Open),
                Int(row.Overdue),
                Int(row.CompletedInRange),
                Hours(row.AvgCompletionHours),
                PercentOrDash(row.OnTimeRate),
            })
            .ToList();

        container.Column(column =>
        {
            column.Item().Element(element => SectionTitle(element, "3. Theo bảng"));
            column.Item().PaddingTop(6).Element(element => Table(element,
                ["Bảng", "Tổng", "Done", "Mở", "Quá hạn", "Xong trong kỳ", "TB giờ", "Đúng hạn"],
                rows,
                widths: [4f, 1f, 1f, 1f, 1.3f, 1.6f, 1.2f, 1.3f]));
        });
    }

    private static void ComposeAssigneeTable(IContainer container, ReportSummary report)
    {
        var rows = report.ByAssignee
            .Select(row => new[]
            {
                string.IsNullOrWhiteSpace(row.AssigneeName) ? ReportUnassigned.Name : row.AssigneeName,
                Int(row.Total),
                Int(row.Done),
                Int(row.Open),
                Int(row.Overdue),
                Int(row.CompletedInRange),
                Hours(row.AvgCompletionHours),
                PercentOrDash(row.OnTimeRate),
            })
            .ToList();

        container.Column(column =>
        {
            column.Item().Element(element => SectionTitle(element, "4. Theo người phụ trách"));
            column.Item().PaddingTop(6).Element(element => Table(element,
                ["Người", "Tổng", "Done", "Mở", "Quá hạn", "Xong trong kỳ", "TB giờ", "Đúng hạn"],
                rows,
                widths: [4f, 1f, 1f, 1f, 1.3f, 1.6f, 1.2f, 1.3f]));
        });
    }

    private static void ComposeActivity(IContainer container, ReportSummary report)
    {
        var activity = report.Activity;

        var rows = activity.ByAction
            .Select(row => new[] { ReportLabels.WithCode(row.Action, ReportLabels.Action(row.Action)), Int(row.Count) })
            .ToList();

        container.Column(column =>
        {
            column.Item().Element(element => SectionTitle(element, "5. Hoạt động trong kỳ"));
            column.Item().PaddingTop(6).Text(
                $"Tổng sự kiện: {activity.TotalActions} · Người dùng hoạt động: {activity.ActiveUsers} · "
                + $"Trung bình/ngày: {Number(activity.ActionsPerDay)}");
            column.Item().PaddingTop(6).Element(element => Table(
                element,
                ["Hành động", "Số lần"],
                rows,
                widths: [6f, 1.2f]));
        });
    }

    private static void ComposeHealth(IContainer container, ReportSummary report)
    {
        var health = report.Health;

        container.Column(column =>
        {
            column.Item().Element(element => SectionTitle(element, "6. Sức khoẻ dự án (AI Observer)"));
            column.Item().PaddingTop(6).Text($"Số lần quét trong kỳ: {health.RunsScanned}");

            column.Item().PaddingTop(6).Element(element => Table(
                element,
                ["Tín hiệu", "Số lượng"],
                health.SignalsByType
                    .Select(row => new[] { ReportLabels.WithCode(row.Type, ReportLabels.SignalType(row.Type)), Int(row.Count) })
                    .ToList(),
                widths: [6f, 1.2f]));

            column.Item().PaddingTop(8).Element(element => Table(
                element,
                ["Mức độ cảnh báo", "Số lượng"],
                health.FindingsBySeverity
                    .Select(row => new[] { ReportLabels.WithCode(row.Severity, ReportLabels.Severity(row.Severity)), Int(row.Count) })
                    .ToList(),
                widths: [6f, 1.2f]));
        });
    }

    private static void ComposeNotes(IContainer container, ReportSummary report)
    {
        container.PaddingTop(6).BorderTop(1).BorderColor(Colors.Grey.Lighten2).Column(column =>
        {
            column.Item().PaddingTop(6).Text(
                $"Sinh tự động lúc {FormatUtc(report.GeneratedAt)} UTC · Không lưu trên server · "
                + "Nguồn: tasks/boards/board_columns/activity_logs/ai_observer_runs.")
                .FontSize(8).FontColor(Colors.Grey.Darken1);

            column.Item().Text($"Phạm vi dữ liệu: {ScopeTitle(report)} · Kỳ báo cáo: {report.Period.Label}")
                .FontSize(8).FontColor(Colors.Grey.Darken1);

            if (report.Truncated.RowCapReached)
            {
                column.Item().PaddingTop(2).Text(
                    $"⚠ Dữ liệu đã được cắt ở {report.Truncated.MaxRows} dòng — số liệu tổng vẫn đầy đủ, "
                    + "chỉ các bảng chi tiết bị giới hạn.")
                    .FontSize(8).SemiBold().FontColor(Colors.Orange.Darken2);
            }
        });
    }

    private static void ComposeFooter(IContainer container, ReportsOptions options)
        => container.PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text(options.CompanyName).FontSize(8).FontColor(Colors.Grey.Darken1);
            row.ConstantItem(120).AlignRight().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(8).FontColor(Colors.Grey.Darken1));
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        });

    // ---- helper trình bày --------------------------------------------------

    private static void SectionTitle(IContainer container, string title)
        => container.Text(title).FontSize(11.5f).Bold().FontColor(Colors.Indigo.Darken3);

    /// <summary>Bảng dữ liệu: header đậm + nền, các dòng có đường kẻ, cột đầu rộng hơn theo <paramref name="widths"/>.</summary>
    private static void Table(
        IContainer container,
        IReadOnlyList<string> headers,
        IReadOnlyList<string[]> rows,
        IReadOnlyList<float> widths)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    columns.RelativeColumn(widths[i]);
                }
            });

            table.Header(header =>
            {
                foreach (var label in headers)
                {
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4)
                        .Text(label).SemiBold().FontSize(8.5f);
                }
            });

            if (rows.Count == 0)
            {
                table.Cell().ColumnSpan((uint)headers.Count).Padding(6)
                    .Text("Không có dữ liệu trong kỳ báo cáo.").FontSize(8.5f).Italic()
                    .FontColor(Colors.Grey.Darken1);
                return;
            }

            foreach (var row in rows)
            {
                for (var i = 0; i < row.Length; i++)
                {
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4)
                        .Text(row[i]).FontSize(8.5f);
                }
            }
        });
    }

    private static string ScopeTitle(ReportSummary report)
        => report.Scope.Type == ReportScopeTypes.Board && !string.IsNullOrWhiteSpace(report.Scope.BoardName)
            ? $"{report.WorkspaceName} › {report.Scope.BoardName}"
            : report.WorkspaceName;

    private static string Definition(ReportSummary report, string key)
        => report.MetricDefinitions.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : Dash;

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(double value) => value.ToString("0.0", Vietnamese);

    /// <summary>Giá trị <c>double?</c> null ⇒ "—" (không in 0 — đã chốt ở §1 D8).</summary>
    private static string Hours(double? value) => value.HasValue ? Number(value.Value) : Dash;

    private static string Percent(double value) => value.ToString("0.0", Vietnamese) + "%";

    private static string PercentOrDash(double? value) => value.HasValue ? Percent(value.Value) : Dash;

    private static string FormatUtc(DateTimeOffset value)
        => value.UtcDateTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private PageSize ResolvePageSize(ReportsOptions options)
    {
        if (!options.TryGetPdfPageSize(out var name))
        {
            _logger.LogWarning(
                "Reports:PdfPageSize='{Configured}' không hợp lệ — dùng A4 (giá trị hợp lệ: A4, A3, Letter).",
                options.PdfPageSize);
        }

        return name switch
        {
            "A3" => PageSizes.A3,
            "Letter" => PageSizes.Letter,
            _ => PageSizes.A4,
        };
    }
}
