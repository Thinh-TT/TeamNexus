using Microsoft.AspNetCore.Identity;

namespace TeamNexus.Persistence.Data;

/// <summary>
/// The three global Identity roles (Admin / Manager / Member) with fixed Guids,
/// seeded into the `roles` table by the initial migration (DB design §3.1 / §6).
/// </summary>
public static class IdentityRoles
{
    public static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly Guid ManagerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly Guid MemberId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static IdentityRole<Guid>[] All { get; } =
    [
        Create("Admin", AdminId),
        Create("Manager", ManagerId),
        Create("Member", MemberId),
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
