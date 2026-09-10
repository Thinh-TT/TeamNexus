using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data;

/// <summary>
/// The single DbContext for the whole modular monolith — Identity + all domain
/// entities — keeping ONE EF Core migration chain (Project-Documents/04-database-design.md §1.1).
/// Column/table names follow snake_case via the EFCore.NamingConventions package.
/// </summary>
public class TeamNexusDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public TeamNexusDbContext(DbContextOptions<TeamNexusDbContext> options)
        : base(options)
    {
    }

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<WorkspaceMember> WorkspaceMembers => Set<WorkspaceMember>();

    public DbSet<Board> Boards => Set<Board>();

    public DbSet<BoardColumn> BoardColumns => Set<BoardColumn>();

    public DbSet<BoardTask> Tasks => Set<BoardTask>();

    public DbSet<Label> Labels => Set<Label>();

    public DbSet<TaskLabel> TaskLabels => Set<TaskLabel>();

    public DbSet<TaskComment> TaskComments => Set<TaskComment>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<AiActionLog> AiActionLogs => Set<AiActionLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TeamNexusDbContext).Assembly);

        // Remaining Identity tables → snake_case names (ApplicationUser is mapped in
        // ApplicationUserConfiguration; keys/indexes/relations keep Identity defaults).
        modelBuilder.Entity<IdentityRole<Guid>>(b =>
        {
            b.ToTable("roles");
            b.HasData(IdentityRoles.All);
        });
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAuditableTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        StampAuditableTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Auto-stamps created_at / updated_at for entities implementing
    /// <see cref="IAuditableEntity"/> (DB design §1.2).
    /// </summary>
    private void StampAuditableTimestamps()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }
}
