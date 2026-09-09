namespace TeamNexus.Persistence.Data;

/// <summary>
/// Marks entities that carry created/updated timestamps, auto-stamped by
/// <see cref="TeamNexusDbContext"/> on SaveChanges (convention per
/// Project-Documents/04-database-design.md §1.2).
/// </summary>
public interface IAuditableEntity
{
    DateTimeOffset CreatedAt { get; set; }

    DateTimeOffset UpdatedAt { get; set; }
}
