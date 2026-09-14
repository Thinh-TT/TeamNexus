namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Configuration the Board module needs in order to build an invitation accept link and to state
/// its lifetime (Phase 11 §3.2).
/// <para>
/// Declared by <b>Board</b> and populated by module <b>Ai</b> (the module that owns the
/// <c>Email</c> configuration section), following the same port/adapter rule as
/// <see cref="IActivityLogWriter"/> and <see cref="IAiAgentResolver"/>: Board must not reference Ai,
/// because Ai already references Board. The default instance is only used by module-scoped tests.
/// </para>
/// </summary>
public sealed class InvitationSettings
{
    /// <summary>Base URL of the SPA, used to build the accept link in the invitation email.</summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";

    /// <summary>Invitation lifetime in days (Email:InvitationExpiryDays, default 7).</summary>
    public int InvitationExpiryDays { get; set; } = 7;

    public TimeSpan InvitationLifetime => TimeSpan.FromDays(Math.Clamp(InvitationExpiryDays, 1, 90));
}
