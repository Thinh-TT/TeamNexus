using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Auth.Options;

namespace TeamNexus.Api.Tests.Infrastructure;

/// <summary>
/// Boots the real ASP.NET Core application in-process for the integration suites (Phase 8 D1).
/// <para>
/// <c>Program</c> already carries the empty <c>public partial class Program</c> that
/// <see cref="WebApplicationFactory{TEntryPoint}"/> needs (Phase 6 D29), so no production change is
/// required here.
/// </para>
/// <para>
/// Every setting is supplied in code — no User Secrets, no <c>appsettings.Development.json</c> —
/// so the suites run on a clean machine and in CI with identical behaviour.
/// </para>
/// </summary>
public sealed class TeamNexusApiFactory : WebApplicationFactory<Program>
{
    private static readonly object Gate = new();

    private static TeamNexusApiFactory? _shared;

    /// <summary>
    /// One host per test process.
    /// <para>
    /// Deliberately NOT one host per test class: <c>WebApplicationFactory</c> builds a full ASP.NET
    /// Core host (DI graph, SignalR, hosted services), so recreating it per class would dominate the
    /// suite's runtime and churn ephemeral state. Per-test isolation comes from
    /// <c>TestScenario.ResetDatabaseAsync</c> (TRUNCATE … CASCADE) and per-client cookie jars.
    /// </para>
    /// </summary>
    public static TeamNexusApiFactory Shared
    {
        get
        {
            lock (Gate)
            {
                if (_shared is null)
                {
                    _shared = new TeamNexusApiFactory { ConnectionString = ResolveConnectionString() };

                    // Touch Services so the host builds here and a config/DI failure surfaces with a
                    // useful stack trace instead of inside an assertion.
                    _ = _shared.Services;

                    AppDomain.CurrentDomain.ProcessExit += (_, _) => _shared?.Dispose();
                }

                return _shared;
            }
        }
    }

    private static string ResolveConnectionString()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("TEAMNEXUS_TEST_DB");

        return string.IsNullOrWhiteSpace(fromEnvironment)
            ? DatabaseFixture.DefaultConnectionString
            : fromEnvironment.Trim();
    }

    /// <summary>
    /// Fixed test signing key: 64 ASCII bytes (&gt; the 32-byte minimum <c>JwtService</c> enforces).
    /// Obviously-fake on purpose so it can never be mistaken for a real secret.
    /// </summary>
    public const string TestSigningKey = "teamnexus-tests-signing-key-0000000000000000000000000000000000000000000000";

    /// <summary>Legacy 32-byte hex key (kept for a negative test that signs with the wrong key).</summary>
    public const string WrongSigningKey = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

    /// <summary>Database used by the suites; supplied by <see cref="DatabaseFixture"/>.</summary>
    public string ConnectionString { get; init; } = DatabaseFixture.DefaultConnectionString;

    /// <summary>
    /// When set, replaces the registered <c>IAiProvider</c> for this host so a suite can script the
    /// model's raw answer (e.g. unparsable JSON) without touching the offline fake's fixed sample.
    /// </summary>
    public ScriptedAiProvider? ScriptedAi { get; init; }

    /// <summary>Overrides <c>Agent:Enabled</c>; the safe off-switch must answer 503 (Phase 7 D18).</summary>
    public bool AgentEnabled { get; init; } = true;

    /// <summary>Overrides <c>Reports:Enabled</c>.</summary>
    public bool ReportsEnabled { get; init; } = true;

    /// <summary>
    /// Overrides <c>AiChat:Enabled</c> (Phase 14 §2.1). Defaults to <c>true</c>: the chat is the
    /// feature under test in its own suite, and it cannot reach the outside world. The suite that proves
    /// the off switch answers 503 builds its own host with <c>false</c>.
    /// </summary>
    public bool AiChatEnabled { get; init; } = true;

    /// <summary>
    /// Overrides <c>Observer:AtRiskDeadlineEnabled</c> (Phase 14 §3.1). Defaults to <c>true</c> — it is
    /// the new detector's own switch and its rules are asserted directly by the pure suite. Suites that
    /// count signals must set it explicitly rather than inherit whatever the host happens to carry.
    /// </summary>
    public bool ObserverAtRiskDeadlineEnabled { get; init; } = true;

    /// <summary>
    /// Overrides <c>Digest:Enabled</c> (Phase 13 §3.3). Defaults to <c>false</c> so no suite can send a
    /// real digest by accident; the digest suites opt in.
    /// </summary>
    public bool DigestEnabled { get; init; }

    /// <summary>
    /// Overrides <c>Digest:SendAtLocalHour</c>. The digest suites set 0 so "today's digest is due" is true
    /// at any wall-clock time, instead of the test having to wait for the production 08:00.
    /// </summary>
    public int DigestSendAtLocalHour { get; init; } = 8;

    /// <summary>
    /// Overrides the clock the app resolves as <see cref="TimeProvider"/> (Phase 12 §P2).
    /// <para>
    /// Registered through <c>ConfigureTestServices</c>, which runs <b>after</b> the app's own
    /// registrations — so this wins even though <c>AddBoardModule</c> also registers
    /// <c>TimeProvider.System</c>. Defaults to the system clock: an ordinary suite keeps real time,
    /// and only the dashboard suite swaps in <see cref="FixedTimeProvider"/>.
    /// </para>
    /// </summary>
    public TimeProvider? Clock { get; init; }

    /// <summary>
    /// Overrides <c>Agent:RunTimeoutSeconds</c>. Defaults to the production 300 s so ordinary suites
    /// can never trip the wall-clock guardrail; the one suite that tests that guardrail lowers it.
    /// </summary>
    public int AgentRunTimeoutSeconds { get; init; } = 300;

    /// <summary>
    /// Overrides <c>Auth:CookieSameSite</c> (Phase 8 D7). Defaults to the production <c>Lax</c>, which
    /// is what local development uses; the cookie-transport suite sets <c>None</c> to prove the value
    /// really comes from configuration instead of being hard-coded.
    /// </summary>
    public string CookieSameSite { get; init; } = "Lax";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Belt and braces: even if the production app checked this, tests must not think they are in
        // Production (which would also wire different cookie policies).
        builder.UseEnvironment(Environments.Development);

        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);

        builder.UseSetting($"{JwtOptions.SectionName}:Issuer", "TeamNexus");
        builder.UseSetting($"{JwtOptions.SectionName}:Audience", "TeamNexus.Web");
        builder.UseSetting($"{JwtOptions.SectionName}:AccessTokenMinutes", "15");
        builder.UseSetting($"{JwtOptions.SectionName}:RefreshTokenDays", "7");
        builder.UseSetting($"{JwtOptions.SectionName}:SigningKey", TestSigningKey);

        // Cookie transport (Phase 8 D7). Defaults to the production value so every suite exercises the
        // same contract local development uses.
        builder.UseSetting("Auth:CookieSameSite", CookieSameSite);

        // OAuth providers stay unconfigured on purpose: AddAuthModule only registers a provider
        // when its credentials exist, so /api/auth/login/{provider} answers 400 deterministically
        // (asserted by the Auth suite) and no test can ever hit a real consent screen.
        builder.UseSetting("Authentication:GitHub:ClientId", string.Empty);
        builder.UseSetting("Authentication:GitHub:ClientSecret", string.Empty);
        builder.UseSetting("Authentication:Google:ClientId", string.Empty);
        builder.UseSetting("Authentication:Google:ClientSecret", string.Empty);

        builder.UseSetting("Frontend:BaseUrl", "http://localhost:5173");
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5173");

        // ---- AI: force the fully offline providers (D5) --------------------------
        // One space, not the empty string: .NET treats an empty environment value as "unset", and
        // the same trick is what keeps offline verification at zero token cost during development.
        builder.UseSetting("DeepSeek:ApiKey", " ");
        builder.UseSetting("Tavily:ApiKey", " ");
        // Phase 11 §2: one space, not the empty string — same trick as the two above. Without it a
        // developer's User Secrets key would make the suite send REAL email on every invitation test.
        builder.UseSetting("Email:ApiKey", " ");
        builder.UseSetting("Agent:Enabled", AgentEnabled ? "true" : "false");
        builder.UseSetting("Reports:Enabled", ReportsEnabled ? "true" : "false");
        builder.UseSetting("Observer:Enabled", "false");

        // Phase 13 §3.3 (P4): the digest is the one feature that e-mails people unprompted, so the suite
        // turns it off explicitly. Without this, a developer's own appsettings/User Secrets value would
        // decide whether the integration suites send real mail — the exact hazard `Email:ApiKey` above
        // already guards against. The digest suites opt back in per test.
        builder.UseSetting("Digest:Enabled", DigestEnabled ? "true" : "false");
        builder.UseSetting(
            "Digest:SendAtLocalHour",
            DigestSendAtLocalHour.ToString(CultureInfo.InvariantCulture));

        // The digest timer must not race the assertions (the same reason the Observer gets a 1-hour delay):
        // the suites call `IDailyDigestRunner.RunOnceAsync` directly instead.
        builder.UseSetting("Digest:StartupDelaySeconds", "3600");

        // The suites assert on behaviour, not on logs; EF's per-statement Information logging would
        // otherwise bury a real failure in thousands of lines.
        builder.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command", "Warning");
        builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Warning");
        builder.UseSetting("Logging:LogLevel:TeamNexus", "Warning");

        // The Observer would otherwise occupy a hosted-service slot and race the assertions; a long
        // startup delay on top of Enabled=false keeps it out of every suite deterministically.
        builder.UseSetting("Observer:StartupDelaySeconds", "3600");

        // Guardrails are exercised explicitly by the Agent suite; test runs must not depend on them.
        builder.UseSetting("Agent:MaxToolCalls", "15");
        builder.UseSetting("Agent:RunTimeoutSeconds", AgentRunTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("Agent:MaxRunTokens", "50000");
        builder.UseSetting("Agent:MaxRunLlmCalls", "20");

        if (ScriptedAi is not null)
        {
            // ConfigureTestServices runs AFTER the app's own registrations, so these replacements are
            // the ones the container resolves (unlike a plain ConfigureServices call).
            //
            // All THREE ports are overridden with the SAME instance (Phase 14 §2.1, P7): the production
            // registration creates a separate stateless transport per port, but a suite that scripts an
            // answer needs every path to see that script — otherwise `IAiStreamingProvider` would keep
            // resolving the offline FakeAiProvider and a chat assertion would silently test the wrong
            // provider.
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAiProvider>(ScriptedAi);
                services.AddSingleton<IAiToolCallingProvider>(ScriptedAi);
                services.AddSingleton<IAiStreamingProvider>(ScriptedAi);
            });
        }

        // Phase 14 §2: the chat is ON by default (it is user-initiated and cannot mail anyone), but the
        // value is stated explicitly here for the same reason `Digest:Enabled` and `Email:ApiKey` are —
        // a developer's own User Secrets must never decide whether an integration suite behaves one way
        // or another. Suites that test the off switch build their own host.
        builder.UseSetting("AiChat:Enabled", AiChatEnabled ? "true" : "false");
        builder.UseSetting("Observer:AtRiskDeadlineEnabled", ObserverAtRiskDeadlineEnabled ? "true" : "false");

        if (Clock is not null)
        {
            // Same "runs last" trick: AddBoardModule registers TimeProvider.System, and this single
            // registration replaces it for the whole host (Phase 12 §P2).
            builder.ConfigureTestServices(services =>
                services.AddSingleton(Clock));
        }
    }
}
