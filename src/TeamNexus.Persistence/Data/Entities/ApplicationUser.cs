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
}
