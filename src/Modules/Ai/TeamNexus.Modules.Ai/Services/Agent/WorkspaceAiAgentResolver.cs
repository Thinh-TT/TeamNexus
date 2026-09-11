using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>
/// Real implementation of the Board module's <see cref="IAiAgentResolver"/> port (Phase 7 D1/D7).
/// <para>
/// The port is declared in Board because Board must not reference Ai; this class is registered by
/// <c>AddAiModule</c>, which runs <b>after</b> <c>AddBoardModule</c> in Program.cs and therefore wins
/// the resolve over <see cref="NullAiAgentResolver"/> (same trick as <c>IActivityLogWriter</c>).
/// </para>
/// <para>
/// The agent is a real <c>users</c> row because <c>tasks.assignee_id</c> and
/// <c>task_comments.author_id</c> both point at <c>users</c>, and it is created <b>lazily</b> so a
/// workspace that never uses the agent never grows one. It cannot log in: no password hash, no email,
/// lockout and 2FA enabled, and no <c>user_logins</c> row.
/// </para>
/// </summary>
public sealed class WorkspaceAiAgentResolver : IAiAgentResolver
{
    private readonly TeamNexusDbContext _db;
    private readonly IBoardEventPublisher _events;
    private readonly AgentOptions _options;
    private readonly ILogger<WorkspaceAiAgentResolver> _logger;

    public WorkspaceAiAgentResolver(
        TeamNexusDbContext db,
        IBoardEventPublisher events,
        IOptions<AgentOptions> options,
        ILogger<WorkspaceAiAgentResolver> logger)
    {
        _db = db;
        _events = events;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Guid> EnsureAgentAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var workspaceExists = await _db.Workspaces
            .AsNoTracking()
            .AnyAsync(w => w.Id == workspaceId, ct);

        if (!workspaceExists)
        {
            throw new NotFoundException("Workspace not found.");
        }

        var existing = await FindAgentUserIdAsync(workspaceId, ct);
        if (existing is not null)
        {
            return existing.Value;
        }

        var displayName = _options.EffectiveAgentDisplayName;
        var userName = BuildUserName(workspaceId);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            // Identity's UserNameIndex is unique on normalized_user_name, so it must be filled in
            // (and is what makes a concurrent double-create collide instead of duplicating).
            NormalizedUserName = userName.ToUpperInvariant(),
            DisplayName = displayName,
            AvatarUrl = string.IsNullOrWhiteSpace(_options.AgentAvatarUrl) ? null : _options.AgentAvatarUrl.Trim(),
            Email = null,
            NormalizedEmail = null,
            EmailConfirmed = false,
            PasswordHash = null,
            PhoneNumber = null,
            PhoneNumberConfirmed = false,
            LockoutEnabled = true,
            LockoutEnd = null,
            TwoFactorEnabled = true,
            AccessFailedCount = 0,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var membership = new WorkspaceMember
        {
            WorkspaceId = workspaceId,
            UserId = user.Id,
            Role = WorkspaceRole.Member,
            MemberType = MemberType.AiAgent,
            AiAgentName = displayName,
            JoinedAt = DateTimeOffset.UtcNow,
        };

        // One transaction so a losing race cannot leave an orphan users row behind: PostgreSQL
        // aborts the whole transaction on the unique violation, and the re-read below takes the winner.
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            _db.Users.Add(user);
            _db.WorkspaceMembers.Add(membership);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "AI Agent created for workspace {WorkspaceId} (userId {AgentUserId}, name '{Name}').",
                workspaceId,
                user.Id,
                displayName);

            return user.Id;
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync(ct);

            // The rollback undid the write, but the entities are still tracked as Added; detaching
            // them keeps the caller's context usable for the re-read.
            _db.Entry(user).State = EntityState.Detached;
            _db.Entry(membership).State = EntityState.Detached;

            var winner = await FindAgentUserIdAsync(workspaceId, ct);
            if (winner is not null)
            {
                _logger.LogInformation(
                    "AI Agent for workspace {WorkspaceId} was created concurrently; using the existing row {AgentUserId}.",
                    workspaceId,
                    winner.Value);

                return winner.Value;
            }

            _logger.LogError(ex, "Could not create the AI Agent for workspace {WorkspaceId}.", workspaceId);
            throw;
        }
    }

    public Task<bool> IsAiAgentAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
        => _db.WorkspaceMembers
            .AsNoTracking()
            .AnyAsync(wm => wm.WorkspaceId == workspaceId
                            && wm.UserId == userId
                            && wm.MemberType == MemberType.AiAgent, ct);

    public async Task<Guid> EnsureClarificationColumnAsync(Guid boardId, CancellationToken ct = default)
    {
        var boardExists = await _db.Boards
            .AsNoTracking()
            .AnyAsync(b => b.Id == boardId, ct);

        if (!boardExists)
        {
            throw new NotFoundException("Board not found.");
        }

        var existing = await _db.BoardColumns
            .AsNoTracking()
            .Where(c => c.BoardId == boardId && c.IsClarification)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            return existing.Value;
        }

        var maxPosition = await _db.BoardColumns
            .Where(c => c.BoardId == boardId)
            .MaxAsync(c => (int?)c.Position, ct) ?? -1;

        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            Name = _options.EffectiveClarificationColumnName,
            Position = maxPosition + 1,
            IsDone = false,
            IsClarification = true,
        };

        try
        {
            _db.BoardColumns.Add(column);
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // The partial unique index uq_board_columns_clarification (or the (board_id, position)
            // unique index) is the race guard: the loser adopts the winning column.
            _db.Entry(column).State = EntityState.Detached;

            var winner = await _db.BoardColumns
                .AsNoTracking()
                .Where(c => c.BoardId == boardId && c.IsClarification)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);

            if (winner is not null)
            {
                _logger.LogInformation(
                    "Clarification column for board {BoardId} was created concurrently; using {ColumnId}.",
                    boardId,
                    winner.Value);

                return winner.Value;
            }

            _logger.LogError(ex, "Could not create the clarification column for board {BoardId}.", boardId);
            throw;
        }

        _logger.LogInformation(
            "Clarification column created for board {BoardId} (columnId {ColumnId}, position {Position}).",
            boardId,
            column.Id,
            column.Position);

        // Announced so open Kanban clients see the new lane without a manual refresh; the publisher
        // is fail-soft, so a SignalR problem can never break moving the task into it.
        try
        {
            await _events.ColumnCreated(boardId, ToResponse(column), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not broadcast ColumnCreated for board {BoardId}.", boardId);
        }

        return column.Id;
    }

    private Task<Guid?> FindAgentUserIdAsync(Guid workspaceId, CancellationToken ct)
        => _db.WorkspaceMembers
            .AsNoTracking()
            .Where(wm => wm.WorkspaceId == workspaceId && wm.MemberType == MemberType.AiAgent)
            .Select(wm => (Guid?)wm.UserId)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Deterministic user name ("ai-agent-&lt;workspaceId:N&gt;"): unique per workspace, traceable in
    /// the database, and the handle the verification harness uses to clean agent rows up.
    /// </summary>
    private static string BuildUserName(Guid workspaceId) => $"ai-agent-{workspaceId:N}";

    private static ColumnResponse ToResponse(BoardColumn column)
        => new(
            column.Id,
            column.BoardId,
            column.Name,
            column.Position,
            column.IsDone,
            column.CreatedAt,
            column.UpdatedAt,
            [],
            column.IsClarification);
}
