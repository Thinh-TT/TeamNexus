using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// The fixed per-workspace vocabulary Jev answers <c>Choice</c> questions against (Giai đoạn 14.7 §A2).
/// <para>
/// Read is Member+ (the vocabulary is part of what every member sees on a card). Replacing it is Manager+,
/// the same gate as every other workspace-configuration write.
/// </para>
/// </summary>
public interface ITaxonomyService
{
    /// <summary>
    /// The workspace vocabulary, seeding the defaults on first read. Member+; unknown workspace ⇒ 404.
    /// Seeding is idempotent and race-safe, so two concurrent first reads cannot duplicate the dictionary.
    /// </summary>
    Task<WorkspaceTaxonomyResponse> GetAsync(
        Guid workspaceId, Guid userId, CancellationToken ct = default);

    /// <summary>Replaces the vocabulary with exactly what the caller sent. Manager+.</summary>
    Task<WorkspaceTaxonomyResponse> ReplaceAsync(
        Guid workspaceId, UpdateTaxonomyRequest request, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// The domain/work-type keys of a workspace, for a caller that only needs the option list (the Jev
    /// triage path). Seeding applies here too, so a board that never opens Settings still gets a vocabulary.
    /// </summary>
    Task<IReadOnlyList<TaxonomySeed>> GetSeedsAsync(
        Guid workspaceId, TaxonomyKind kind, CancellationToken ct = default);
}

public sealed class TaxonomyService : ITaxonomyService
{
    /// <summary>PostgreSQL <c>unique_violation</c> — what a lost seeding race raises.</summary>
    private const string UniqueViolation = "23505";

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly ILogger<TaxonomyService> _logger;

    public TaxonomyService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        ILogger<TaxonomyService> logger)
    {
        _db = db;
        _access = access;
        _logger = logger;
    }

    public async Task<WorkspaceTaxonomyResponse> GetAsync(
        Guid workspaceId, Guid userId, CancellationToken ct = default)
    {
        await _access.RequireMemberAsync(workspaceId, userId, ct);

        var seeded = await EnsureSeededAsync(workspaceId, ct);

        return await BuildResponseAsync(workspaceId, seeded, ct);
    }

    public async Task<IReadOnlyList<TaxonomySeed>> GetSeedsAsync(
        Guid workspaceId, TaxonomyKind kind, CancellationToken ct = default)
    {
        // No membership check here on purpose: this overload is called from paths that already resolved the
        // board and proved membership, and a second check would be a second database round-trip for a
        // question that was already answered.
        await EnsureSeededAsync(workspaceId, ct);

        var entries = await LoadAsync(workspaceId, kind, ct);

        return entries.Count == 0
            // Defensive: seeding just ran, so an empty result means the rows vanished between the two
            // statements. Falling back to the defaults keeps dispatching working rather than silently
            // offering the model an empty option list (which would make every `choice` answer unusable).
            ? TaxonomyDefaults.For(kind)
            : entries.Select(entry => new TaxonomySeed(entry.Key, entry.Label, entry.ColorHex)).ToList();
    }

    public async Task<WorkspaceTaxonomyResponse> ReplaceAsync(
        Guid workspaceId, UpdateTaxonomyRequest request, Guid userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var domains = Validate(request.Domains, TaxonomyKind.Domain, "Domains");
        var workTypes = Validate(request.WorkTypes, TaxonomyKind.WorkType, "WorkTypes");

        if (domains.Count == 0 || workTypes.Count == 0)
        {
            // An empty vocabulary would leave every `Choice` question with no options, which silently turns
            // the whole dispatching feature off. Refusing is louder than accepting and degrading.
            throw new BadRequestException(
                "Mỗi workspace phải có ít nhất một domain và một work type.");
        }

        // Replace semantics, in one transaction: delete then insert, so a failure half way cannot leave the
        // workspace with a partially replaced dictionary.
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        await _db.WorkspaceTaxonomies
            .Where(x => x.WorkspaceId == workspaceId)
            .ExecuteDeleteAsync(ct);

        var position = 0;
        foreach (var entry in domains)
        {
            _db.WorkspaceTaxonomies.Add(NewEntry(workspaceId, TaxonomyKind.Domain, entry, position++));
        }

        position = 0;
        foreach (var entry in workTypes)
        {
            _db.WorkspaceTaxonomies.Add(NewEntry(workspaceId, TaxonomyKind.WorkType, entry, position++));
        }

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "Taxonomy replaced for workspace {WorkspaceId} by user {UserId}: {Domains} domain(s), {WorkTypes} work type(s).",
            workspaceId, userId, domains.Count, workTypes.Count);

        // `Seeded: false` because this is a deliberate curation, not the default seed.
        return await BuildResponseAsync(workspaceId, seeded: false, ct);
    }

    // ---- seeding --------------------------------------------------------------------------------

    /// <summary>
    /// Seeds the defaults when the workspace has no entries at all, and reports whether THIS call is what
    /// created them.
    /// <para>
    /// <b>Two concurrent first reads are the normal case, not an edge case</b> — a workspace's first visit
    /// can easily be two people (or two browser tabs) at once, and both observe an empty table.
    /// </para>
    /// <para>
    /// <b>⛔ Why this is one <c>INSERT … ON CONFLICT DO NOTHING</c> statement per kind and NOT 13 tracked
    /// <c>Add</c> calls.</b> The tracked version was tried first and produced a <c>40P01 deadlock</c> under
    /// exactly this test: two transactions inserting the same 13 rows take the index locks in different
    /// orders, PostgreSQL kills one, and the caller gets a 500. The deadlock is also <i>not</i> catchable by
    /// a <c>23505</c> guard, so a "catch the unique violation and re-read" design silently leaves a
    /// production-reachable 500.
    /// </para>
    /// <para>
    /// A single conflict-tolerant statement removes the whole class of problem: the second writer simply
    /// affects 0 rows for the keys that already exist, needs no savepoint, pollutes no ChangeTracker state
    /// (the caller's context stays usable for the re-read that follows), and cannot deadlock because it is
    /// one command. The unique index is still what makes it correct under concurrency — the statement relies
    /// on it rather than replacing it.
    /// </para>
    /// </summary>
    private async Task<bool> EnsureSeededAsync(Guid workspaceId, CancellationToken ct)
    {
        var alreadyHasEntries = await _db.WorkspaceTaxonomies
            .AsNoTracking()
            .AnyAsync(x => x.WorkspaceId == workspaceId, ct);

        if (alreadyHasEntries)
        {
            return false;
        }

        var inserted = 0;

        foreach (var kind in new[] { TaxonomyKind.Domain, TaxonomyKind.WorkType })
        {
            var position = 0;

            foreach (var seed in TaxonomyDefaults.For(kind))
            {
                // Parameterised throughout (ExecuteSqlInterpolated), so a taxonomy key can never become SQL.
                inserted += await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     INSERT INTO workspace_taxonomies
                         (id, workspace_id, kind, key, label, color_hex, position, created_at, updated_at)
                     VALUES
                         ({Guid.NewGuid()}, {workspaceId}, {ToWireKind(kind)}, {seed.Key}, {seed.Label},
                          {seed.ColorHex}, {position}, {DateTimeOffset.UtcNow}, {DateTimeOffset.UtcNow})
                     ON CONFLICT (workspace_id, kind, key) DO NOTHING
                     """,
                    ct);

                position++;
            }
        }

        if (inserted == 0)
        {
            // Another request won every key. Report "not seeded by us" so the response is honest.
            _logger.LogInformation(
                "Taxonomy for workspace {WorkspaceId} was seeded concurrently; using the existing rows.",
                workspaceId);

            return false;
        }

        _logger.LogInformation(
            "Taxonomy seeded with defaults for workspace {WorkspaceId} ({Inserted} row(s)).",
            workspaceId, inserted);

        return true;
    }

    /// <summary>Lowercase wire value of a kind, matching the CHECK constraint.</summary>
    private static string ToWireKind(TaxonomyKind kind)
        => kind == TaxonomyKind.WorkType ? "work_type" : "domain";

    // ---- read / map -----------------------------------------------------------------------------

    private Task<List<WorkspaceTaxonomy>> LoadAsync(
        Guid workspaceId, TaxonomyKind kind, CancellationToken ct)
        => _db.WorkspaceTaxonomies
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.Kind == kind)
            .OrderBy(x => x.Position)
            .ThenBy(x => x.Key)
            .ToListAsync(ct);

    private async Task<WorkspaceTaxonomyResponse> BuildResponseAsync(
        Guid workspaceId, bool seeded, CancellationToken ct)
    {
        var all = await _db.WorkspaceTaxonomies
            .AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderBy(x => x.Position)
            .ThenBy(x => x.Key)
            .ToListAsync(ct);

        return new WorkspaceTaxonomyResponse(
            workspaceId,
            all.Where(x => x.Kind == TaxonomyKind.Domain).Select(ToResponse).ToList(),
            all.Where(x => x.Kind == TaxonomyKind.WorkType).Select(ToResponse).ToList(),
            seeded);
    }

    private static WorkspaceTaxonomy NewEntry(
        Guid workspaceId, TaxonomyKind kind, TaxonomyEntryRequest entry, int position)
        => new()
        {
            WorkspaceId = workspaceId,
            Kind = kind,
            Key = entry.Key.Trim(),
            Label = entry.Label.Trim(),
            ColorHex = string.IsNullOrWhiteSpace(entry.ColorHex) ? null : entry.ColorHex.Trim(),
            Position = position,
        };

    private static TaxonomyEntryResponse ToResponse(WorkspaceTaxonomy entry)
        => new(
            entry.Id,
            entry.Kind == TaxonomyKind.WorkType ? "work_type" : "domain",
            entry.Key,
            entry.Label,
            entry.ColorHex,
            entry.Position);

    // ---- validation -----------------------------------------------------------------------------

    /// <summary>
    /// Validates one kind's entries, normalising as it goes. Throws <see cref="BadRequestException"/> (400)
    /// on the first violation, so nothing is written when the request is rejected.
    /// <para>
    /// Public for the same reason <c>WorkspaceMemberService.ParseRole</c> is: these rules are the interesting
    /// part of the feature and a pure test must be able to pin them without a database.
    /// </para>
    /// </summary>
    public static List<TaxonomyEntryRequest> Validate(
        IReadOnlyList<TaxonomyEntryRequest>? entries, TaxonomyKind kind, string fieldName)
    {
        var list = entries ?? [];
        var kindName = kind == TaxonomyKind.WorkType ? "work type" : "domain";

        if (list.Count > TaxonomyDefaults.MaxEntriesPerKind)
        {
            throw new BadRequestException(
                $"{fieldName}: tối đa {TaxonomyDefaults.MaxEntriesPerKind} {kindName} "
                + $"(nhận {list.Count}).");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<TaxonomyEntryRequest>(list.Count);

        foreach (var entry in list)
        {
            if (entry is null)
            {
                throw new BadRequestException($"{fieldName}: mục null không hợp lệ.");
            }

            var key = entry.Key?.Trim();
            var label = entry.Label?.Trim();

            // Trimmed BEFORE validating: the pattern is anchored, so testing the raw value would reject a
            // client that padded its JSON with spaces ("  #1677ff  ") for no real reason.
            var color = string.IsNullOrWhiteSpace(entry.ColorHex) ? null : entry.ColorHex.Trim();

            if (string.IsNullOrEmpty(key) || key.Length > 50)
            {
                throw new BadRequestException($"{fieldName}: 'key' phải có 1–50 ký tự.");
            }

            if (string.IsNullOrEmpty(label) || label.Length > 120)
            {
                throw new BadRequestException($"{fieldName}: 'label' của '{key}' phải có 1–120 ký tự.");
            }

            // Duplicate keys would violate uq_workspace_taxonomies_key, and the resulting 500 would hide a
            // plain client mistake. A 400 naming the offender is the useful answer.
            if (!seen.Add(key))
            {
                throw new BadRequestException($"{fieldName}: key '{key}' bị lặp.");
            }

            if (color is not null && !TaxonomyDefaults.IsValidColor(color))
            {
                throw new BadRequestException(
                    $"{fieldName}: 'colorHex' của '{key}' phải theo dạng #RRGGBB.");
            }

            normalized.Add(new TaxonomyEntryRequest(key, label, color));
        }

        return normalized;
    }
}
