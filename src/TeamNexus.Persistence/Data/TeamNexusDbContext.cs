using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Persistence.Data;

/// <summary>
/// The single DbContext for the whole modular monolith — Identity + all domain
/// entities — keeping ONE EF Core migration chain (/04-database-design.md §1.1).
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

    // AI Observer (Phase 5 §1): activity event store + recipient-scoped alerts + run audit.
    public DbSet<ActivityLog> Activities => Set<ActivityLog>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<AiObserverRun> AiObserverRuns => Set<AiObserverRun>();

    // AI Agent Executor (Phase 7 §2): append-only run journal + agent output files.
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();

    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();

    // Member management & email (Phase 11 §1): workspace invitations + transactional-email log.
    public DbSet<WorkspaceInvitation> WorkspaceInvitations => Set<WorkspaceInvitation>();

    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();

    // AI Agent Chat (Phase 14.5 §B): append-only board-scoped chat transcript. `task_id` is the
    // reserved half of the scope CHECK, so a later phase can move the task tab onto server storage
    // without another migration.
    public DbSet<AgentChatMessage> AgentChatMessages => Set<AgentChatMessage>();

    // Giai đoạn 14.7 §A2: the fixed per-workspace vocabulary Jev answers `Choice` questions against.
    public DbSet<WorkspaceTaxonomy> WorkspaceTaxonomies => Set<WorkspaceTaxonomy>();

    // Giai đoạn 14.7 Track B §B3 (Workspace RAG): the indexed documents and their embedded slices.
    // The `vector` extension they need is declared in OnModelCreating below, and created by the migration
    // with an explicit `CREATE EXTENSION IF NOT EXISTS` — see the remarks there.
    public DbSet<WorkspaceKnowledgeDocument> WorkspaceKnowledgeDocuments => Set<WorkspaceKnowledgeDocument>();

    public DbSet<WorkspaceKnowledgeChunk> WorkspaceKnowledgeChunks => Set<WorkspaceKnowledgeChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Giai đoạn 14.7 Track B §B3: `vector` is a third-party extension (NOT a PostgreSQL contrib module),
        // so the official `postgres` image does not ship it — CI and local dev must use
        // `pgvector/pgvector:pg18` (project-docs/02-tech-stack-decisions.md §3).
        //
        // It is declared here so the requirement lives in the model, but the migration carries an explicit
        // `CREATE EXTENSION IF NOT EXISTS vector;`: EF Core only records this as an annotation, and a schema
        // that silently depends on an operator having run that statement by hand is exactly the kind of
        // "works on my machine" failure this declaration is meant to prevent.
        modelBuilder.HasPostgresExtension("vector");

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
