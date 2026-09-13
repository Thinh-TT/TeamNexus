using Microsoft.AspNetCore.Identity;

namespace TeamNexus.Persistence.Data;

/// <summary>
/// The two global Identity roles (Admin / User) with fixed Guids,
/// seeded into the `roles` table by Phase 9 migration (DB design §3.1).
///
/// Admin  = system administrator of the platform (assign manually).
/// User   = every regular user who signs in via OAuth (default).
///
/// Workspace-level permissions (Admin/Manager/Member per workspace)
/// are stored in workspace_members.role — NOT here.
/// </summary>
public static class IdentityRoles
{
    public static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static IdentityRole<Guid>[] All { get; } =
    [
        Create("Admin", AdminId),
        Create("User", UserId),
    ];

    private static IdentityRole<Guid> Create(string name, Guid id) => new(name)
    {
        Id = id,
        NormalizedName = name.ToUpperInvariant(),
        // IdentityRole initializes ConcurrencyStamp with a random Guid per instance;
        // HasData seeds must be deterministic or every model build looks like a
        // pending change. Using the role's own id keeps it stable and unique.
        ConcurrencyStamp = id.ToString("D"),
    };
}

