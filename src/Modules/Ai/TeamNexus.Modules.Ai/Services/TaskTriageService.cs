using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Services.Jev;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;
using BoardEntity = TeamNexus.Persistence.Data.Entities.Board;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Jev-backed task triage and smart assignee dispatching (Giai đoạn 14.7 §A3/§A4).
/// <para>
/// This is the seam that turns a workspace's stored configuration into the typed <c>state</c> and
/// <c>questions</c> Jev needs. It owns the two things the decision service deliberately does not: reading
/// the database, and mapping Jev's opaque labels back onto real rows.
/// </para>
/// <para>
/// <b>Nothing here writes.</b> Both operations are pure suggestions; applying one is an ordinary task edit
/// by the user. That is why neither of them enters the Accountability Layer: there is no business write for
/// it to account for.
/// </para>
/// </summary>
public interface ITaskTriageService
{
    /// <summary>
    /// Suggests domain, work type, priority and a security-review flag for a task that does not exist yet.
    /// Member+; unknown board ⇒ 404; <c>Jev:Enabled = false</c> ⇒ 503.
    /// </summary>
    Task<TaskTriageResponse> TriageAsync(
        Guid boardId, TaskTriageRequest request, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Ranks workspace members for an existing task. Member+; the task must live on
    /// <paramref name="boardId"/>.
    /// </summary>
    Task<AssigneeSuggestionResponse> SuggestAssigneesAsync(
        Guid boardId, Guid taskId, Guid userId, CancellationToken ct = default);
}

public sealed class TaskTriageService : ITaskTriageService
{
    /// <summary>Matches the <c>tasks.title</c> column limit enforced by <c>TaskService</c>.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>Matches <c>tasks.description</c>'s validation in <c>TaskService</c>.</summary>
    public const int MaxDescriptionLength = 4_000;

    /// <summary>Tasks already finished are not counted as load.</summary>
    private const int LoadCandidateLimit = 500;

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly ITaxonomyService _taxonomies;
    private readonly IJevDecisionService _jev;
    private readonly Jev.JevOptions _options;
    private readonly ILogger<TaskTriageService> _logger;

    public TaskTriageService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        ITaxonomyService taxonomies,
        IJevDecisionService jev,
        Microsoft.Extensions.Options.IOptions<Jev.JevOptions> options,
        ILogger<TaskTriageService> logger)
    {
        _db = db;
        _access = access;
        _taxonomies = taxonomies;
        _jev = jev;
        _options = options.Value;
        _logger = logger;
    }

    // ---- §A3 triage ---------------------------------------------------------------------------

    public async Task<TaskTriageResponse> TriageAsync(
        Guid boardId, TaskTriageRequest request, Guid userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 503 before any read: a disabled feature must not reveal which board exists.
        if (!_options.Enabled)
        {
            throw new JevDisabledException();
        }

        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == boardId, ct)
            ?? throw new NotFoundException("Board not found.");

        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title) || title.Length > MaxTitleLength)
        {
            throw new BadRequestException($"Tiêu đề phải có 1–{MaxTitleLength} ký tự.");
        }

        if (request.Description is { Length: > MaxDescriptionLength })
        {
            throw new BadRequestException($"Mô tả tối đa {MaxDescriptionLength} ký tự.");
        }

        // The vocabulary comes from the workspace, not from a constant: a workspace that curated its own
        // dictionary must get ITS options, otherwise the answer would name a domain nobody uses.
        var domains = await _taxonomies.GetSeedsAsync(board.WorkspaceId, TaxonomyKind.Domain, ct);
        var workTypes = await _taxonomies.GetSeedsAsync(board.WorkspaceId, TaxonomyKind.WorkType, ct);

        var context = new TaskTriageContext(
            title,
            request.Description,
            domains.Select(seed => new Jev.TaxonomyEntry(seed.Key, seed.Label)).ToList(),
            workTypes.Select(seed => new Jev.TaxonomyEntry(seed.Key, seed.Label)).ToList());

        var result = await _jev.TriageTaskAsync(context, ct);

        var response = new TaskTriageResponse(
            result.SuggestedDomain,
            result.SuggestedWorkType,
            result.SuggestedPriority,
            result.NeedsSecurityReview,
            result.ConfidenceScore,
            result.RoutingAdvice,
            result.Options.Select(option => new TriageOptionResponse(option.Field, option.Value, option.Probability)).ToList(),
            // The provider failure is already absorbed into "ManualRequired + nothing offered"; this flag
            // exists so the UI can say "AI tạm không khả dụng" instead of implying the AI looked and found
            // nothing, which are very different messages to a user.
            Degraded: result.RoutingAdvice == Jev.ConfidenceRouting.ManualRequired,
            Model: _options.EffectiveModel,
            LatencyMs: result.LatencyMs);

        _logger.LogDebug(
            "Triage for board {BoardId}: advice={Advice}, confidence={Confidence}, degraded={Degraded}.",
            boardId, result.RoutingAdvice, result.ConfidenceScore, response.Degraded);

        return response;
    }

    // ---- §A4 assignee dispatch ----------------------------------------------------------------

    public async Task<AssigneeSuggestionResponse> SuggestAssigneesAsync(
        Guid boardId, Guid taskId, Guid userId, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            throw new JevDisabledException();
        }

        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == boardId, ct)
            ?? throw new NotFoundException("Board not found.");

        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);

        // The Tasks query filter makes a soft-deleted task a 404, and the board check keeps a proposal
        // inside the board the caller is actually looking at (the RequestTaskScopedAsync precedent).
        var task = await _db.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        if (task.BoardId != boardId)
        {
            throw new BadRequestException("Thẻ không thuộc bảng này.");
        }

        var candidates = await LoadCandidatesAsync(board.WorkspaceId, ct);

        if (candidates.Count == 0)
        {
            // No candidates means no `Choice` options, and a question with no options is unanswerable. Report
            // it honestly instead of spending a call the provider can only fail.
            return new AssigneeSuggestionResponse(null, 0d, Jev.ConfidenceRouting.ManualRequired, [], false, _options.EffectiveModel, 0);
        }

        var context = new AssigneeDispatchContext(
            task.Title,
            task.Description,
            candidates.Select(c => new MemberWorkloadContext(
                c.Label, c.UserId, c.DisplayName, c.PrimaryRole, c.Skills,
                c.CapacityWeight, c.MaxWipLimit, c.OpenTaskCount, c.OverdueCount, c.InProgressCount)).ToList());

        var result = await _jev.RecommendAssigneeAsync(context, ct);

        var response = new AssigneeSuggestionResponse(
            result.RecommendedUserId,
            result.ConfidenceScore,
            result.RoutingAdvice,
            result.CandidateRankings
                .Select(rank => new CandidateSuggestionResponse(
                    rank.UserId, rank.DisplayName, rank.Probability, rank.Reason))
                .ToList(),
            Degraded: result.RoutingAdvice == Jev.ConfidenceRouting.ManualRequired,
            Model: _options.EffectiveModel,
            LatencyMs: result.LatencyMs);

        _logger.LogDebug(
            "Assignee suggestion for task {TaskId}: advice={Advice}, confidence={Confidence}, candidates={CandidateCount}.",
            taskId, result.RoutingAdvice, result.ConfidenceScore, candidates.Count);

        return response;
    }

    /// <summary>
    /// Builds one candidate row per actionable workspace member, with a server-issued opaque label.
    /// <para>
    /// <b>The label is the anti-leak device.</b> Jev only ever sees <c>cand-1</c>, <c>cand-2</c>, … so the
    /// <c>Guid</c> it "chooses" is really a label we map back in-process. That is why this feature can claim
    /// 0 % identifier leakage structurally rather than by filtering afterwards.
    /// </para>
    /// <para>
    /// The workspace's own AI Agent is a legitimate candidate — dispatching work to it is the point of
    /// Phase 7 — and it is identified by <c>MemberType</c>, never by name.
    /// </para>
    /// </summary>
    private async Task<List<Candidate>> LoadCandidatesAsync(Guid workspaceId, CancellationToken ct)
    {
        var members = await _db.WorkspaceMembers
            .AsNoTracking()
            .Where(wm => wm.WorkspaceId == workspaceId)
            .Include(wm => wm.User)
            .OrderBy(wm => wm.JoinedAt)
            .ToListAsync(ct);

        var candidateMembers = members
            .Where(wm => wm.User is not null)
            .Take(Math.Max(1, _options.Effective.MaxCandidates))
            .ToList();

        if (candidateMembers.Count == 0)
        {
            return [];
        }

        var userIds = candidateMembers.Select(wm => wm.UserId).ToList();
        var now = DateTimeOffset.UtcNow;

        // One bounded read for the whole load picture, ranked in memory afterwards: `tasks.priority` is text
        // and the "is done" test lives on the column, so a per-member aggregate query would be several
        // round-trips for a number this small.
        var openTasks = await _db.Tasks
            .AsNoTracking()
            .Where(t => t.Board!.WorkspaceId == workspaceId
                        && t.AssigneeId != null
                        && userIds.Contains(t.AssigneeId.Value)
                        && (t.Column == null || !t.Column.IsDone)
                        && t.CompletedAt == null)
            .Select(t => new
            {
                AssigneeId = t.AssigneeId!.Value,
                t.DueDate,
                InProgress = t.Column != null && !t.Column.IsDone && !t.Column.IsClarification,
            })
            .Take(LoadCandidateLimit)
            .ToListAsync(ct);

        var load = openTasks
            .GroupBy(t => t.AssigneeId)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    Open = group.Count(),
                    Overdue = group.Count(t => t.DueDate.HasValue && t.DueDate.Value < now),
                    InProgress = group.Count(t => t.InProgress),
                });

        var candidates = new List<Candidate>(candidateMembers.Count);

        for (var index = 0; index < candidateMembers.Count; index++)
        {
            var member = candidateMembers[index];
            var stats = load.GetValueOrDefault(member.UserId);

            candidates.Add(new Candidate(
                // 1-based so the label reads as "candidate #1" in a log or a prompt.
                Label: $"cand-{index + 1}",
                UserId: member.UserId,
                DisplayName: member.User!.DisplayName,
                PrimaryRole: member.PrimaryRole,
                Skills: member.Skills.ToList(),
                CapacityWeight: member.CapacityWeight,
                MaxWipLimit: member.MaxWipLimit,
                OpenTaskCount: stats?.Open ?? 0,
                OverdueCount: stats?.Overdue ?? 0,
                InProgressCount: stats?.InProgress ?? 0));
        }

        return candidates;
    }

    /// <summary>Internal row: a member plus their measured load, before it becomes a Jev context record.</summary>
    private sealed record Candidate(
        string Label,
        Guid UserId,
        string DisplayName,
        string PrimaryRole,
        List<string> Skills,
        double CapacityWeight,
        int MaxWipLimit,
        int OpenTaskCount,
        int OverdueCount,
        int InProgressCount);
}
