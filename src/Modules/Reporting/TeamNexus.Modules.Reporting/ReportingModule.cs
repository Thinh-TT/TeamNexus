using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Modules.Reporting.Contracts;
using TeamNexus.Modules.Reporting.Endpoints;
using TeamNexus.Modules.Reporting.Options;
using TeamNexus.Modules.Reporting.Services;

namespace TeamNexus.Modules.Reporting;

/// <summary>
/// DI registration entry point cho module Reporting (Phase 6).
/// <para>
/// §1 (đợt này) = <see cref="ReportAggregator"/> — **hàm thuần static**, không cần DI; phần đăng ký ở đây
/// vì vậy chỉ gồm options + endpoint filter, cộng 2 extension point
/// (<see cref="AddReportingModule"/> / <see cref="MapReportingModuleEndpoints"/>) để §3/§5 chỉ việc cắm
/// thêm — <c>Program.cs</c> không phải sửa thêm lần nào.
/// </para>
/// <para>
/// Chưa có ở §1 (cố ý): <c>ReportService</c> (§3) và renderer QuestPDF/ClosedXML (§4 — cũng là lúc thêm 2
/// <c>PackageReference</c> tương ứng vào csproj), cùng <c>MapReportingEndpoints</c> (§5).
/// </para>
/// </summary>
public static class ReportingModule
{
    public static IServiceCollection AddReportingModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ---- Options (Phase 6 §2.1) -----------------------------------------
        // Không ValidateOnStart: config sai chỉ rơi về default (xem ReportsOptions.ToThresholds clamp),
        // để app vẫn boot được khi mới deploy mà chưa có section "Reports".
        services.AddOptions<ReportsOptions>()
            .Bind(configuration.GetSection(ReportsOptions.SectionName));

        // ---- Endpoint filters (resolved from DI) -----------------------------
        // DomainExceptionFilter (module Board) map BoardModuleException → { error, status }; §3 sẽ tái dùng
        // bằng cách cho exception của Reporting kế thừa BoardModuleException.
        services.AddTransient<DomainExceptionFilter>();

        // ---- Report service (Phase 6 §3) -------------------------------------
        // Scoped: nơi duy nhất chạm DbContext (đọc).
        services.AddScoped<IReportService, ReportService>();

        // ---- Renderer PDF/Excel (Phase 6 §4) ---------------------------------
        // Đăng ký ngay tại đây (không phụ thuộc caller gọi đúng thứ tự): nếu chỉ dựa vào fallback bên dưới
        // thì một lần đăng ký sai ở Program.cs sẽ khiến production phục vụ bản no-op ⇒ export 500.
        // License QuestPDF (Community) được set trong ReportPdfRenderer để renderer tự đủ.
        services.AddScoped<IReportPdfRenderer, ReportPdfRenderer>();
        services.AddScoped<IReportExcelRenderer, ReportExcelRenderer>();
        services.AddScoped<IReportExportService, ReportExportService>();

        // Fallback renderer: chỉ được thêm vào khi chưa có implementation thật, để service graph luôn hợp lệ
        // (host validate OnBuild — thiếu implementation sẽ làm hỏng cả `dotnet ef` design-time host).
        if (!services.Any(d => d.ServiceType == typeof(IReportExportService)))
        {
            services.AddScoped<IReportExportService, NoReportExportService>();
        }

        LogResolvedConfiguration(configuration);

        return services;
    }

    /// <summary>
    /// Cắm endpoint group của module; thân hàm gọi <c>ReportingEndpoints.MapReportingEndpoints()</c> —
    /// §4 tạo khung, §5 chỉ việc thêm route vào đó nên <c>Program.cs</c> không phải sửa lại.
    /// </summary>
    public static IEndpointRouteBuilder MapReportingModuleEndpoints(this IEndpointRouteBuilder endpoints)
        => endpoints.MapReportingEndpoints();

    /// <summary>
    /// Một dòng log lúc khởi động để thấy ngay module có bật không và cấu hình đang dùng — không chứa
    /// secret (cùng cách <c>AiModule.LogResolvedConfiguration</c>, Phase 5 §5.4).
    /// </summary>
    private static void LogResolvedConfiguration(IConfiguration configuration)
    {
        var options = configuration.GetSection(ReportsOptions.SectionName).Get<ReportsOptions>()
                      ?? new ReportsOptions();

        // LoggerFactory tạm: Program.cs chưa cấu hình logging tại thời điểm đăng ký module.
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
        var logger = loggerFactory.CreateLogger("TeamNexus.Modules.Reporting");

        logger.LogInformation(
            "Reporting module: enabled={Enabled}, defaultRange={DefaultRangeDays}d, maxRange={MaxRangeDays}d, "
            + "maxExportRows={MaxExportRows}, maxActions={MaxActionsPerReport}, pdfPageSize={PdfPageSize}.",
            options.Enabled,
            options.DefaultRangeDays,
            options.MaxRangeDays,
            options.MaxExportRows,
            options.MaxActionsPerReport,
            options.PdfPageSize);
    }
}
