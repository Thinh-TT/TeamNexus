using Microsoft.AspNetCore.Identity;

namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Application user (ASP.NET Core Identity, Guid keys) with TeamNexus-specific fields.
/// Table: users (Phase 1 §2 / DB design §3.1).
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public string? AvatarUrl { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Whether the daily work digest email may be sent to this user (Phase 13 §3).
    /// <para>
    /// The flag lives in the database — not in browser storage — because a
    /// <c>BackgroundService</c> decides who gets a digest, and it runs on the server with no
    /// browser in reach.
    /// </para>
    /// <para>
    /// Defaults to <c>true</c> and the column carries <c>DEFAULT true</c>, so every account that
    /// predates the migration keeps the behaviour it already had and no backfill is needed.
    /// </para>
    /// <para>
    /// <b>Not</b> an <see cref="IAuditableEntity"/>: the <c>users</c> table has no
    /// <c>updated_at</c> column (Phase 1 schema), so exactly this one column changes when the user
    /// toggles it.
    /// </para>
    /// </summary>
    public bool DigestEnabled { get; set; } = true;
}
