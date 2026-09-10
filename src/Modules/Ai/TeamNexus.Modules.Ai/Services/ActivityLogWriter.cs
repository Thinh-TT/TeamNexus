using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// EF Core adapter for the Board module's <see cref="IActivityLogWriter"/> port (Phase 5 §2.2):
/// appends one row to <c>activity_logs</c> for each board mutation that Board reports.
/// <para>
/// Registered by <c>AddAiModule</c>, overriding the Board module's no-op. This is why the port
/// lives in Board: Board never references Ai, so there is no circular module dependency.
/// </para>
/// <para>
/// <b>Never throws.</b> Recording activity is a best-effort side effect — a failure here must not
/// break the CRUD request that caused it (same rule as <c>IBoardEventPublisher</c>). The only
/// exception re-thrown is caller cancellation, which is not a logging failure.
/// </para>
/// <para>
/// Uses the request-scoped <see cref="TeamNexusDbContext"/> on purpose: every call site is placed
/// <b>after</b> the business write has been saved/committed and the change tracker is clean, so
/// <c>SaveChangesAsync</c> here only flushes the new log row. If a future hook needs to record
/// activity in the middle of a transaction, switch to an <c>IDbContextFactory</c>-created context.
/// </para>
/// </summary>
public sealed class ActivityLogWriter : IActivityLogWriter
{
    /// <summary>Matches the entity/config max length of <c>action</c> and <c>entity_type</c> (Phase 5 §1).</summary>
    public const int MaxTextLength = 64;

    private readonly TeamNexusDbContext _db;
    private readonly ILogger<ActivityLogWriter> _logger;

    public ActivityLogWriter(TeamNexusDbContext db, ILogger<ActivityLogWriter> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RecordAsync(ActivityLogEntry entry, CancellationToken ct = default)
    {
        try
        {
            if (!IsRecordable(entry, out var reason))
            {
                _logger.LogWarning(
                    "Skipped activity log: {Reason} (action={Action}, entityType={EntityType}).",
                    reason,
                    entry.Action,
                    entry.EntityType);
                return;
            }

            _db.Activities.Add(new ActivityLog
            {
                WorkspaceId = entry.WorkspaceId,
                BoardId = entry.BoardId,
                UserId = entry.UserId,
                EntityType = entry.EntityType,
                EntityId = entry.EntityId,
                Action = entry.Action,
                // An empty payload is stored as null rather than a meaningless "{}".
                Payload = string.IsNullOrWhiteSpace(entry.PayloadJson) ? null : entry.PayloadJson,
            });

            await _db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller cancellation is not a logging failure — let it propagate.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to record activity {Action} for {EntityType} {EntityId}.",
                entry.Action,
                entry.EntityType,
                entry.EntityId);
        }
    }

    /// <summary>
    /// Defensive validation before touching the database: the columns are <c>varchar(64)</c> and
    /// required, so an invalid entry is dropped with a warning instead of throwing.
    /// </summary>
    private static bool IsRecordable(ActivityLogEntry entry, out string reason)
    {
        if (string.IsNullOrWhiteSpace(entry.Action) || entry.Action.Length > MaxTextLength)
        {
            reason = $"invalid action (must be 1-{MaxTextLength} characters)";
            return false;
        }

        if (string.IsNullOrWhiteSpace(entry.EntityType) || entry.EntityType.Length > MaxTextLength)
        {
            reason = $"invalid entity type (must be 1-{MaxTextLength} characters)";
            return false;
        }

        if (entry.WorkspaceId == Guid.Empty)
        {
            reason = "missing workspace id";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
