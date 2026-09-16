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
using TeamNexus.Modules.Ai.Services.Agent;
using TeamNexus.Modules.Ai.Services.Agent.Agents;
using TeamNexus.Modules.Ai.Services.Appliers;
using TeamNexus.Modules.Ai.Services.Email;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Modules.Board.Services;
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

    /// <summary>Name of the <see cref="IHttpClientFactory"/> client used for Tavily web search (Phase 7 §4.3).</summary>
    public const string TavilyHttpClientName = "Tavily";

    /// <summary>Name of the <see cref="IHttpClientFactory"/> client used for Resend email (Phase 11 §2).</summary>
    public const string EmailHttpClientName = "Resend";

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

        // ---- Activity log (Phase 5 §2) ---------------------------------------
        // Real adapter for the port declared by the Board module (which registers a no-op
        // default). This line must stay AFTER AddBoardModule in Program.cs to win the resolve.
        services.AddScoped<IActivityLogWriter, ActivityLogWriter>();
        // ---- AI Observer (Phase 5 §5.4) --------------------------------------
        // Options are bound here (the POCO itself lives in §3); the notification writer and the
        // scan service are scoped, and the periodic driver is a hosted service so it starts with
        // the host. ObserverBackgroundService only needs IServiceScopeFactory, so scoped services
        // resolve inside its own scope (no captive dependency).
        services.AddOptions<ObserverOptions>()
            .Bind(configuration.GetSection(ObserverOptions.SectionName));
        services.AddScoped<INotificationService, NotificationService>();

        // User-facing alerts (Phase 11 §6.4): Board declares the port + a no-op default, this line
        // overrides it — the same ordering rule as IEmailGateway/IActivityLogWriter above.
        services.AddScoped<INotificationWriter, NotificationWriter>();
        services.AddScoped<IObserverService, ObserverService>();
        services.AddHostedService<ObserverBackgroundService>();

        // ---- Transactional email (Phase 11 §2) --------------------------------
        // Same switch shape as DeepSeek/Tavily: no key ⇒ the offline NullEmailSender, so invitations
        // and quick emails stay fully exercisable in dev and CI can never send real mail.
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName));

        services.AddHttpClient(EmailHttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<EmailOptions>>().Value;
            client.Timeout = options.Timeout;
        });

        services.AddScoped<IEmailSender>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<EmailOptions>>().Value;

            return options.HasApiKey
                ? ActivatorUtilities.CreateInstance<ResendEmailSender>(provider)
                : ActivatorUtilities.CreateInstance<NullEmailSender>(provider);
        });

        // The one gateway every transactional email goes through (audit row + per-workspace quota).
        services.AddScoped<IEmailDispatcher, EmailDispatcher>();

        // ---- Daily digest (Phase 13 §3.3) --------------------------------------
        // The digest is the only feature here that mails people unprompted, so it ships switched OFF
        // (DigestOptions.Enabled defaults to false): turning it on is one explicit setting in production,
        // and a fresh clone / a test run / CI can never send mail by accident.
        services.AddOptions<DigestOptions>()
            .Bind(configuration.GetSection(DigestOptions.SectionName));

        // Renders the digest and hands it to the one dispatcher that owns the email_messages audit row.
        services.AddScoped<IDigestGateway, DigestGateway>();

        // The pass itself is a scoped service so a test can run it directly — no timer, no waiting.
        services.AddScoped<IDailyDigestRunner, DailyDigestRunner>();

        // ... and the hosted service only decides WHEN to ask for it.
        services.AddHostedService<DailyDigestBackgroundService>();

        // Board's email port (Phase 11 §2, decision D9): Board declares + registers NullEmailGateway,
        // and this line — which MUST stay after AddBoardModule in Program.cs — overrides it, exactly
        // like IActivityLogWriter and IAiAgentResolver above.
        services.AddScoped<IEmailGateway, EmailGateway>();

        // Board's invitation settings (accept-link base URL + lifetime) are owned by the Email
        // section, so Ai supplies them too — same override rule.
        services.AddScoped(provider =>
        {
            var email = provider.GetRequiredService<IOptions<EmailOptions>>().Value;

            return new WorkspaceEmailOptions
            {
                MaxRecipientsPerQuickEmail = email.MaxRecipientsPerQuickEmail,
                MaxEmailsPerHourPerWorkspace = email.MaxEmailsPerHourPerWorkspace,
            };
        });

        services.AddScoped(provider =>
        {
            var email = provider.GetRequiredService<IOptions<EmailOptions>>().Value;
            var frontend = provider.GetRequiredService<IConfiguration>().GetSection("Frontend:BaseUrl").Value;

            return new InvitationSettings
            {
                FrontendBaseUrl = string.IsNullOrWhiteSpace(frontend)
                    ? "http://localhost:5173"
                    : frontend,
                InvitationExpiryDays = email.InvitationExpiryDays,
            };
        });

        // ---- AI Agent Executor (Phase 7 §4.11) -------------------------------
        // §4.11 registers the agent's options, transports, tools, orchestrator and reaper here so
        // Program.cs never changes again (it already calls AddAiModule + MapAiModuleEndpoints).
        services.AddOptions<AgentOptions>()
            .Bind(configuration.GetSection(AgentOptions.SectionName));
        services.AddOptions<TavilyOptions>()
            .Bind(configuration.GetSection(TavilyOptions.SectionName));

        // Separate named client: Tavily has its own host, timeout and (optional) auth header.
        services.AddHttpClient(TavilyHttpClientName, (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<TavilyOptions>>().Value;
            client.Timeout = options.Timeout;
        });

        // Function-calling port: the same HasApiKey switch as IAiProvider. Registered separately, so
        // the two interfaces get two instances — acceptable because both providers are stateless
        // transports (the FakeAiProvider scenario is derived from the message history, not from state).
        services.AddSingleton<IAiToolCallingProvider>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<DeepSeekOptions>>().Value;

            return options.HasApiKey
                ? ActivatorUtilities.CreateInstance<DeepSeekAiProvider>(provider)
                : ActivatorUtilities.CreateInstance<FakeAiProvider>(provider);
        });

        // Web search: real Tavily when a key is configured, offline sample results otherwise.
        services.AddSingleton<IWebSearchProvider>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<TavilyOptions>>().Value;

            return options.HasApiKey
                ? ActivatorUtilities.CreateInstance<TavilyWebSearchProvider>(provider)
                : ActivatorUtilities.CreateInstance<FakeWebSearchProvider>(provider);
        });

        // Agent identity port (Phase 7 D7): Board declares it + registers NullAiAgentResolver, and this
        // line — which MUST stay after AddBoardModule in Program.cs — overrides it, exactly like
        // IActivityLogWriter above.
        services.AddScoped<IAiAgentResolver, WorkspaceAiAgentResolver>();

        // Tool whitelist: one registration per tool + the dispatcher.
        services.AddScoped<IAgentTool, SearchSystemDataTool>();
        services.AddScoped<IAgentTool, WebSearchTool>();
        services.AddScoped<IAgentTool, DraftOutputTool>();
        services.AddScoped<IAgentTool, RequestClarificationTool>();
        services.AddScoped<IAgentToolRegistry, AgentToolRegistry>();

        // Agent output → Accountability Layer, and the two appliers that apply it once approved.
        services.AddScoped<IAgentOutputService, AgentOutputService>();
        services.AddScoped<IAiActionApplier, PostCommentApplier>();
        services.AddScoped<IAiActionApplier, PostAttachmentApplier>();

        // Orchestrator: registered as the concrete type too, because the background scope resolves it
        // directly to acquire the per-task advisory lock on that scope's own connection.
        services.AddScoped<AgentRunOrchestrator>();
        services.AddScoped<IAgentRunService>(provider => provider.GetRequiredService<AgentRunOrchestrator>());
        services.AddScoped<IAgentAttachmentService, AgentAttachmentService>();

        // Singleton: the cancellation/reservation table must be shared by every request scope, and the
        // reaper must run even when Agent:Enabled is false.
        services.AddSingleton<AgentRunCancellationRegistry>();
        services.AddHostedService<AgentRunReaper>();


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
    /// Maps Ai module endpoint groups: the Smart Setup proposal endpoint (Phase 3 §3.2), the
    /// Accountability Layer endpoints (Phase 4 §3) and the AI Observer + notification endpoints
    /// (Phase 5 §5).
    /// </summary>
    public static IEndpointRouteBuilder MapAiModuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapSmartSetupEndpoints();
        endpoints.MapAiActionEndpoints();
        endpoints.MapObserverEndpoints();
        endpoints.MapNotificationEndpoints();
        endpoints.MapAgentRunEndpoints();
        endpoints.MapAttachmentEndpoints();

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

        // Observer configuration (Phase 5 §5.4): one line so it is obvious at a glance whether the
        // periodic scan is on and how aggressive it is. Contains no secrets.
        var observer = configuration.GetSection(ObserverOptions.SectionName).Get<ObserverOptions>()
                       ?? new ObserverOptions();

        logger.LogInformation(
            "Ai module: Observer enabled={Enabled}, interval={Interval}, lookback={LookbackHours}h, "
            + "dedupe={DeduplicationWindowHours}h, minSeverity={MinSeverityToNotify}.",
            observer.Enabled,
            observer.Interval,
            observer.LookbackHours,
            observer.DeduplicationWindowHours,
            observer.MinSeverityToNotify);

        // AI Agent Executor (Phase 7 §4.11): exactly ONE line, so it is obvious at a glance whether
        // the agent is on and how aggressive its guardrails are. Contains no secret.
        var agent = configuration.GetSection(AgentOptions.SectionName).Get<AgentOptions>()
                    ?? new AgentOptions();
        var tavily = configuration.GetSection(TavilyOptions.SectionName).Get<TavilyOptions>()
                     ?? new TavilyOptions();
        var effectiveAgent = agent.Effective;

        logger.LogInformation(
            "Ai module: Agent enabled={Enabled}, provider={Provider}, webSearch={WebSearch}, "
            + "maxToolCalls={MaxToolCalls}, timeout={TimeoutSeconds}s, tokenBudget={MaxRunTokens}, "
            + "llmCalls={MaxRunLlmCalls}, maxAttachmentKB={MaxAttachmentKB}, tavilyAuth={TavilyAuthMode}.",
            agent.Enabled,
            options.HasApiKey ? nameof(DeepSeekAiProvider) : nameof(FakeAiProvider),
            tavily.HasApiKey ? nameof(TavilyWebSearchProvider) : nameof(FakeWebSearchProvider),
            effectiveAgent.MaxToolCalls,
            effectiveAgent.RunTimeoutSeconds,
            effectiveAgent.MaxRunTokens,
            effectiveAgent.MaxRunLlmCalls,
            effectiveAgent.MaxAttachmentBytes / 1024,
            tavily.UseBearerAuth ? TavilyOptions.AuthModeBearer : TavilyOptions.AuthModeBody);

        // Transactional email (Phase 11 §2): whether real mail can leave the process, and the
        // quota guardrail that protects the free tier. Contains no secret.
        var email = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>()
                    ?? new EmailOptions();

        logger.LogInformation(
            "Ai module: Email provider={Provider}, from={FromAddress}, invitationExpiryDays={ExpiryDays}, "
            + "quickEmailMaxRecipients={MaxRecipients}, hourlyCap={HourlyCap}.",
            email.HasApiKey ? nameof(ResendEmailSender) : nameof(NullEmailSender),
            email.FromAddress,
            email.InvitationExpiryDays,
            email.MaxRecipientsPerQuickEmail,
            email.MaxEmailsPerHourPerWorkspace);

        // Daily digest (Phase 13 §3): exactly ONE line so it is obvious at a glance whether the only
        // unprompted-email feature in the codebase is on and when it fires. Contains no secret.
        var digest = configuration.GetSection(DigestOptions.SectionName).Get<DigestOptions>()
                     ?? new DigestOptions();

        logger.LogInformation(
            "Ai module: Digest enabled={Enabled}, sendAt={Hour:00}:{Minute:00} (UTC+{OffsetMinutes}), "
            + "pollInterval={PollInterval}, maxUsersPerRun={MaxUsers}, maxDailyEmails={MaxEmails}.",
            digest.Enabled,
            digest.EffectiveSendAtLocalHour,
            digest.EffectiveSendAtLocalMinute,
            digest.EffectiveTimeZoneOffsetMinutes,
            digest.PollInterval,
            digest.EffectiveMaxUsersPerRun,
            digest.EffectiveMaxDailyEmails);
    }
}
