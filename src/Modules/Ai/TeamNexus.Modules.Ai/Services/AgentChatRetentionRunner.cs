using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Persistence.Data;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Deletes chat turns older than <c>AgentChat:RetentionDays</c> (Phase 14.5 §B).
/// <para>
/// <b>One sweep for both conversations.</b> The predicate is <c>created_at</c> across the whole table, so a
/// board's turns and a task's turns (Phase 14.6 §A3) age out together — the window is a property of "a chat
/// transcript", not of which scope wrote it, and two owners for one table is how a table ends up with rows
/// nothing prunes.
/// </para>
/// <b>Why a chat needs retention at all.</b> Every other append-only table in this schema either belongs
/// to an existing prune (the Observer's runs) or stores something a human explicitly asked to keep
/// (attachments, comments). A chat transcript is neither: it is unbounded prose that grows on every
/// question, so without a sweep the free-tier database would fill with conversations nobody will re-read.
/// </para>
/// <para>
/// <b>Why it is a scoped runner and not a hosted service.</b> Same reasoning as
/// <c>DailyDigestRunner</c>: the work is idempotent and batch-bounded, it is directly callable from a test
/// (which is how its behaviour is verified at all), and one fewer background timer is one fewer thing that
/// can misfire on a host that sleeps.
/// </para>
/// <para>
/// <b>Batch-bounded, always.</b> The first sweep on a long-lived database could face millions of rows, and
/// a single unbounded <c>DELETE</c> would lock the table for the duration. Rows are removed oldest-first
/// in <c>RetentionBatchSize</c> chunks, and the loop stops as soon as a chunk comes back short — so a run
/// costs at most one extra batch after the data is gone.
/// </para>
/// </summary>
public sealed class AgentChatRetentionRunner
{
    private readonly TeamNexusDbContext _db;
    private readonly AgentChatOptions _options;
    private readonly ILogger<AgentChatRetentionRunner> _logger;

    public AgentChatRetentionRunner(
        TeamNexusDbContext db,
        IOptions<AgentChatOptions> options,
        ILogger<AgentChatRetentionRunner> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Removes every turn older than the retention window and returns how many rows were deleted.
    /// <paramref name="now"/> is a parameter rather than a clock read so a test can move time instead of
    /// waiting for it — the same reason <c>AgentGuardrails</c> takes <c>Elapsed</c>.
    /// </summary>
    public async Task<int> RunOnceAsync(DateTimeOffset? now = null, CancellationToken ct = default)
    {
        var effective = _options.Effective;
        var cutoff = (now ?? DateTimeOffset.UtcNow).AddDays(-effective.RetentionDays);

        var deletedTotal = 0;

        while (!ct.IsCancellationRequested)
        {
            // Select-then-delete by id rather than a bulk `ExecuteDelete` with a limit: the provider has no
            // portable "delete with limit", and taking the ids first keeps the batch exactly bounded.
            var ids = await _db.AgentChatMessages
                .AsNoTracking()
                .Where(m => m.CreatedAt < cutoff)
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.Id)
                .Take(effective.RetentionBatchSize)
                .Select(m => m.Id)
                .ToListAsync(ct);

            if (ids.Count == 0)
            {
                break;
            }

            var deleted = await _db.AgentChatMessages
                .Where(m => ids.Contains(m.Id))
                .ExecuteDeleteAsync(ct);

            deletedTotal += deleted;

            if (ids.Count < effective.RetentionBatchSize)
            {
                break;
            }
        }

        if (deletedTotal > 0)
        {
            _logger.LogInformation(
                "Agent chat retention removed {Count} message(s) older than {Cutoff} "
                + "(RetentionDays={Days}).",
                deletedTotal,
                cutoff,
                effective.RetentionDays);
        }

        return deletedTotal;
    }
}
