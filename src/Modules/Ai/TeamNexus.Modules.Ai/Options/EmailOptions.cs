namespace TeamNexus.Modules.Ai.Options;

/// <summary>
/// Transactional email configuration (Phase 11 §2). Section <c>Email</c> in appsettings.
/// <para>
/// <b>The API key is a secret.</b> It lives in User Secrets locally and in an environment variable
/// on the host — never in <c>appsettings.json</c> (02-tech-stack-decisions §2.9). An unset key is a
/// supported state: the module then registers <c>NullEmailSender</c>, so the whole invitation and
/// quick-email flow stays exercisable offline (and CI can never send real mail).
/// </para>
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Resend API key. Empty/whitespace ⇒ the offline <c>NullEmailSender</c> is used.</summary>
    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.resend.com";

    /// <summary>Sender identity. The Resend onboarding address works without a verified domain.</summary>
    public string FromAddress { get; set; } = "TeamNexus <onboarding@resend.dev>";

    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Invitation lifetime (DB design §3.3 says "mặc định 7 ngày").</summary>
    public int InvitationExpiryDays { get; set; } = 7;

    /// <summary>Cap on one "quick email" fan-out (protects the free-tier quota).</summary>
    public int MaxRecipientsPerQuickEmail { get; set; } = 50;

    /// <summary>Per-workspace hourly cap across every email kind.</summary>
    public int MaxEmailsPerHourPerWorkspace { get; set; } = 100;

    /// <summary>True when a key is configured — the same switch shape as DeepSeek/Tavily.</summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 120));

    public string NormalizedBaseUrl => BaseUrl.TrimEnd('/');

    public TimeSpan InvitationLifetime => TimeSpan.FromDays(Math.Clamp(InvitationExpiryDays, 1, 90));
}
