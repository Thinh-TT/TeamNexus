using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Endpoints;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Ai.Services.Appliers;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Ai;

/// <summary>
/// DI registration entry point for the Ai module (modular monolith pattern, Phase 3).
/// <para>
/// §1: <see cref="DeepSeekOptions"/>, the named <see cref="HttpClientName"/> HttpClient and the
/// endpoint filters reused from Board/Shared. §2: <see cref="IAiProvider"/> + its two
/// implementations (DeepSeek / Fake). The Smart Setup service and endpoint follow in §4;
/// <see cref="MapAiModuleEndpoints"/> already exists as the extension point they hook into,
/// so Program.cs never has to change again.
/// </para>
/// </summary>
public static class AiModule
{
    /// <summary>Name of the <see cref="IHttpClientFactory"/> client used for DeepSeek calls.</summary>
    public const string HttpClientName = "DeepSeek";

    public static IServiceCollection AddAiModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ---- Options (Phase 3 §1.1) -----------------------------------------
        // Không ValidateOnStart: cho phép dev chạy khi chưa có DeepSeek:ApiKey.
        services.AddOptions<DeepSeekOptions>()
            .Bind(configuration.GetSection(DeepSeekOptions.SectionName));

        // ---- HTTP client (Phase 3 §1.2) -------------------------------------
        // Named client (không phải typed client) vì IAiProvider được đăng ký theo
        // cấu hình ở §2 (Fake vs DeepSeek) — tránh lệch lifetime giữa typed client
        // (transient) và đăng ký tường minh. Timeout lấy từ cấu hình ngay tại đây
        // để §2 chỉ việc IHttpClientFactory.CreateClient(AiModule.HttpClientName).
        services.AddHttpClient(HttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<DeepSeekOptions>>().Value;
            client.Timeout = options.Timeout;
        });

        // ---- AI provider (Phase 3 §2) ---------------------------------------
        // Chọn theo cấu hình: chưa có key → FakeAiProvider (dev offline, không tốn phí);
        // có key → DeepSeekAiProvider. Provider là stateless transport nên dùng singleton;
        // §4 SmartSetupService (scoped) phụ thuộc singleton là hợp lệ.
        services.AddSingleton<IAiProvider>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<DeepSeekOptions>>().Value;

            return options.HasApiKey
                ? ActivatorUtilities.CreateInstance<DeepSeekAiProvider>(provider)
                : ActivatorUtilities.CreateInstance<FakeAiProvider>(provider);
        });

        // ---- Smart Setup service (Phase 3 §3.2/§4) ---------------------------
        // Registered here so the endpoint (and its filters) can resolve it; the generation
        // logic lands in §4.3.
        services.AddScoped<ISmartSetupService, SmartSetupService>();

        // ---- Accountability Layer (Phase 4 §2) -------------------------------
        // AiActionService is the single gateway for AI write actions; every action type plugs in
        // through its own IAiActionApplier (adding one = one class + one registration line).
        services.AddScoped<IAiActionService, AiActionService>();
        services.AddScoped<IAiActionApplier, CreateSubtasksApplier>();

        // ---- Endpoint filters (resolved from DI) -----------------------------
        // DomainExceptionFilter (Board) maps BoardModuleException → { error, status }
        // (bao gồm AiProviderException 502); AntiforgeryValidationEndpointFilter (Shared)
        // enforces X-XSRF-TOKEN on POST.
        services.AddTransient<DomainExceptionFilter>();
        services.AddTransient<AntiforgeryValidationEndpointFilter>();

        LogResolvedConfiguration(services, configuration);

        return services;
    }

    /// <summary>
    /// Maps Ai module endpoint groups: the Smart Setup proposal endpoint (Phase 3 §3.2) and the
    /// Accountability Layer endpoints (Phase 4 §3).
    /// </summary>
    public static IEndpointRouteBuilder MapAiModuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapSmartSetupEndpoints();
        endpoints.MapAiActionEndpoints();

        return endpoints;
    }

    /// <summary>
    /// Logs which provider configuration is active so a missing key is obvious in the
    /// console without ever touching the key value itself (Phase 3 §1.1/§2.2/§7).
    /// </summary>
    private static void LogResolvedConfiguration(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(DeepSeekOptions.SectionName).Get<DeepSeekOptions>()
                      ?? new DeepSeekOptions();

        // Temporary logger factory: Program.cs does not configure logging until Build(),
        // which happens after module registration. Used once, for startup diagnostics.
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
        var logger = loggerFactory.CreateLogger("TeamNexus.Modules.Ai");

        if (!options.HasApiKey)
        {
            logger.LogWarning(
                "Ai module: DeepSeek:ApiKey chưa được cấu hình — đăng ký FakeAiProvider (proposal mẫu, không gọi API). "
                + "Đặt key bằng: dotnet user-secrets set --project src/TeamNexus.Api \"DeepSeek:ApiKey\" \"sk-...\"");
        }
        else
        {
            logger.LogInformation(
                "Ai module: DeepSeek đã cấu hình (provider=DeepSeekAiProvider, model={Model}, baseUrl={BaseUrl}, timeout={TimeoutSeconds}s).",
                options.Model,
                options.NormalizedBaseUrl,
                options.TimeoutSeconds);
        }
    }
}
