namespace TeamNexus.Modules.Ai.Options;

/// <summary>
/// Daily-digest configuration (Phase 13 §3.3), section <c>"Digest"</c> in appsettings / environment
/// variables — the same shape as <c>ObserverOptions</c>/<c>AgentOptions</c>: a plain POCO bound from
/// configuration with no hard validation at startup, so a missing section simply yields the documented
/// defaults and every value is clamped before use.
/// <para>
/// <b><see cref="Enabled"/> defaults to <c>false</c>, and that is deliberate.</b> The digest is the only
/// feature in this codebase that emails people <i>unprompted</i>; opting in at the configuration level
/// means a fresh clone, a test run and CI can never send mail by accident, and enabling it in production
/// is one explicit setting rather than an implicit side effect of the module being loaded.
/// </para>
/// </summary>
public sealed class DigestOptions
{
    public const string SectionName = "Digest";

    /// <summary>
    /// Master switch. <c>false</c> ⇒ the runner reports <c>Skipped</c> and no e-mail is composed at all.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Hour of day (0–23) at which a digest is considered "due", in the recipient's own timezone.</summary>
    public int SendAtLocalHour { get; set; } = 8;

    /// <summary>Minute of that hour (0–59).</summary>
    public int SendAtLocalMinute { get; set; }

    /// <summary>
    /// Timezone used to decide which calendar day a digest belongs to, as <c>UTC + this many minutes</c>.
    /// <para>
    /// One global value on purpose: a per-user timezone would be a second column on <c>users</c> that
    /// nothing in the requirements asks for. 420 = UTC+7, which is where this deployment's users are.
    /// </para>
    /// </summary>
    public int DefaultTimeZoneOffsetMinutes { get; set; } = 420;

    /// <summary>Grace period after startup so the first digest does not compete with cold start.</summary>
    public int StartupDelaySeconds { get; set; } = 120;

    /// <summary>How often the runner wakes up to check whether a digest is due. Clamped to 1..1440 minutes.</summary>
    public int PollIntervalMinutes { get; set; } = 60;

    /// <summary>Cost/abuse control: how many recipients one run may consider.</summary>
    public int MaxUsersPerRun { get; set; } = 200;

    /// <summary>How many workspaces one digest may cover (the rest wait for the next day).</summary>
    public int MaxWorkspacesPerUser { get; set; } = 10;

    /// <summary>Task rows per bucket in the email. The counts stay exact; only the listing is capped.</summary>
    public int MaxItemsPerBucket { get; set; } = 5;

    /// <summary>
    /// Hard cap on e-mails actually sent in one run.
    /// <para>
    /// <b>Separate from the quick-email quota on purpose.</b> <c>WorkspaceEmailOptions</c> caps a workspace
    /// at 100 e-mails per hour; a digest fans out to every member, so reusing that budget would let one
    /// 100-person workspace lock itself out of its own digest.
    /// </para>
    /// </summary>
    public int MaxDailyEmails { get; set; } = 500;

    // ---- derived ---------------------------------------------------------------

    /// <summary>Poll period, clamped to a sane range (1 minute .. 1 day).</summary>
    public TimeSpan PollInterval => TimeSpan.FromMinutes(Math.Clamp(PollIntervalMinutes, 1, 24 * 60));

    /// <summary>Timezone offset, clamped to the real-world range (UTC−14 .. UTC+14).</summary>
    public int EffectiveTimeZoneOffsetMinutes
        => Math.Clamp(DefaultTimeZoneOffsetMinutes, -14 * 60, 14 * 60);

    /// <summary>Send hour, clamped to a real hour of the day.</summary>
    public int EffectiveSendAtLocalHour => Math.Clamp(SendAtLocalHour, 0, 23);

    /// <summary>Send minute, clamped to a real minute of the hour.</summary>
    public int EffectiveSendAtLocalMinute => Math.Clamp(SendAtLocalMinute, 0, 59);

    /// <summary>Workspace cap, never below 1 (0 would silently disable the feature).</summary>
    public int EffectiveMaxWorkspacesPerUser => Math.Max(1, MaxWorkspacesPerUser);

    /// <summary>Per-bucket item cap, never below 1.</summary>
    public int EffectiveMaxItemsPerBucket => Math.Max(1, MaxItemsPerBucket);

    /// <summary>Recipient cap per run, never below 1.</summary>
    public int EffectiveMaxUsersPerRun => Math.Max(1, MaxUsersPerRun);

    /// <summary>Daily send cap, never below 1.</summary>
    public int EffectiveMaxDailyEmails => Math.Max(1, MaxDailyEmails);

    /// <summary>
    /// The instant at which a digest becomes due for the day <paramref name="todayLocal"/>.
    /// <para>
    /// Stated as an offset <b>from the start of the recipient's day</b> rather than as a UTC instant,
    /// because "8 giờ sáng" has to mean 8 a.m. where the reader is. The runner compares the current
    /// time against this value and only sends once it has passed.
    /// </para>
    /// </summary>
    public DateTimeOffset DueAt(int timeZoneOffsetMinutes, DateOnly todayLocal)
    {
        var localMidnightUtc = new DateTimeOffset(
            todayLocal.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddMinutes(-timeZoneOffsetMinutes);

        return localMidnightUtc
            .AddHours(EffectiveSendAtLocalHour)
            .AddMinutes(EffectiveSendAtLocalMinute);
    }
}
