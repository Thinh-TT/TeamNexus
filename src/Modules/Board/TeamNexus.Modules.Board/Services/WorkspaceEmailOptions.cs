namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// The email quota/limits the Board module needs to police its own fan-out (Phase 11 §4.1).
/// <para>
/// Declared by <b>Board</b> and populated by module <b>Ai</b> (which owns the <c>Email</c> section),
/// exactly like <see cref="InvitationSettings"/>. The default instance is a conservative fallback
/// used when the Ai module is not registered (module-scoped tests): a small cap is the safe
/// direction for a limit whose purpose is protecting a metered quota.
/// </para>
/// </summary>
public sealed class WorkspaceEmailOptions
{
    /// <summary>Maximum recipients in one "quick email" fan-out.</summary>
    public int MaxRecipientsPerQuickEmail { get; set; } = 50;

    /// <summary>Per-workspace hourly cap across every email kind (invitations included).</summary>
    public int MaxEmailsPerHourPerWorkspace { get; set; } = 100;
}
