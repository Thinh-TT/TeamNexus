using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Contracts;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Smart Setup orchestration (Phase 3 §4.3): builds the prompt from board/workspace context,
/// calls <see cref="IAiProvider"/>, then parses/normalizes/resolves the AI output into a
/// <see cref="SmartSetupProposal"/> for human review. Never writes to the database — applying a
/// confirmed proposal belongs to the Accountability Layer (Phase 4).
/// </summary>
public interface ISmartSetupService
{
    /// <summary>
    /// Generates a proposal for <paramref name="boardId"/>. Requires Manager/Admin in the
    /// board's workspace (403), 404 when the board is unknown, 400 on an invalid description
    /// or when the AI produced no usable sub-task.
    /// </summary>
    Task<SmartSetupProposal> GenerateAsync(
        Guid boardId,
        SmartSetupRequest request,
        Guid userId,
        CancellationToken ct = default);
}

public sealed class SmartSetupService : ISmartSetupService
{
    /// <summary>Bound on the team lead's description (Phase 3 §4.3 step 3 / §7).</summary>
    public const int MaxDescriptionLength = 4000;

    /// <summary>Normalization bounds (Phase 3 §4.3 step 9).</summary>
    public const int MaxTitleLength = 200;

    public const int MaxTaskDescriptionLength = 2000;

    public const int MaxLabelsPerTask = 5;

    public const int MaxLabelLength = 80;

    private static readonly string[] ValidPriorities = ["Low", "Medium", "High", "Urgent"];

    /// <summary>Raw AI output is JSON; read it case-insensitively (camelCase come what may).</summary>
    private static readonly JsonSerializerOptions AiOutputJson = new(JsonSerializerDefaults.Web);

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IWorkspaceMemberService _members;
    private readonly IColumnService _columns;
    private readonly IAiProvider _aiProvider;
    private readonly DeepSeekOptions _options;
    private readonly ILogger<SmartSetupService> _logger;

    public SmartSetupService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IWorkspaceMemberService members,
        IColumnService columns,
        IAiProvider aiProvider,
        IOptions<DeepSeekOptions> options,
        ILogger<SmartSetupService> logger)
    {
        _db = db;
        _access = access;
        _members = members;
        _columns = columns;
        _aiProvider = aiProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SmartSetupProposal> GenerateAsync(
        Guid boardId,
        SmartSetupRequest request,
        Guid userId,
        CancellationToken ct = default)
    {
        // 1. Board must exist and be visible (soft-deleted boards are filtered out by EF).
        var board = await _db.Boards
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == boardId, ct)
            ?? throw new NotFoundException("Board not found.");

        // 2. Manager/Admin of the board's workspace (plain members get 403).
        await _access.RequireManagerAsync(board.WorkspaceId, userId, ct);

        // 3. Validate the description before spending any tokens.
        var description = ValidateDescription(request.Description);

        // 4. Context: column names, workspace members, existing labels.
        var columns = await _columns.GetColumnsAsync(boardId, userId, ct);
        var members = await _members.GetMembersAsync(board.WorkspaceId, userId, ct);
        var labels = await _db.Labels
            .Where(l => l.WorkspaceId == board.WorkspaceId)
            .OrderBy(l => l.Name)
            .Select(l => new WorkspaceLabel(l.Id, l.Name))
            .AsNoTracking()
            .ToListAsync(ct);

        // 5-6. Prompts.
        var boardName = string.IsNullOrWhiteSpace(board.Name) ? "(không tên)" : board.Name;
        var systemPrompt = SmartSetupPrompts.BuildSystemPrompt();
        var userPrompt = SmartSetupPrompts.BuildUserPrompt(
            boardName,
            columns.Select(c => c.Name).ToList(),
            members,
            labels.Select(l => l.Name).ToList(),
            description);

        // 7-8. Call the provider, parse the JSON, retry once with a stricter prompt on bad JSON.
        var output = await CompleteWithJsonRetryAsync(systemPrompt, userPrompt, ct);

        // 9-11. Normalize + resolve into the API contract.
        var proposal = BuildProposal(output, members, labels, _options.MaxTaskCount);

        _logger.LogInformation(
            "Smart Setup proposal generated for board {BoardId}: {TaskCount} task(s), {MatchedAssignees} matched assignee(s).",
            boardId,
            proposal.Tasks.Count,
            proposal.Tasks.Count(t => t.Assignee?.Matched == true));

        // 12. In-memory only — Phase 3 never persists a proposal.
        return proposal;
    }

    // ---- provider call + parse ---------------------------------------------

    /// <summary>
    /// Steps 7–8: one completion in JSON mode; on a malformed answer, ask once more with the parse
    /// error appended to the prompt. Token cost of the retry is bounded by <c>MaxTokens</c>.
    /// </summary>
    private async Task<AiSmartSetupOutput> CompleteWithJsonRetryAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct)
    {
        var completion = await _aiProvider.CompleteAsync(
            new AiCompletionRequest(systemPrompt, userPrompt, _options.Temperature, _options.MaxTokens, JsonMode: true),
            ct);

        try
        {
            return ParseOutput(completion.Content);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "AI trả về JSON không hợp lệ — thử lại một lần với prompt chặt hơn.");

            var retryPrompt = SmartSetupPrompts.BuildRetryUserPrompt(userPrompt, ex.Message);
            var retry = await _aiProvider.CompleteAsync(
                new AiCompletionRequest(systemPrompt, retryPrompt, _options.Temperature, _options.MaxTokens, JsonMode: true),
                ct);

            try
            {
                return ParseOutput(retry.Content);
            }
            catch (JsonException retryEx)
            {
                throw new AiProviderException(
                    "Không parse được JSON từ AI sau 2 lần thử (schema không khớp). "
                    + $"Chi tiết: {retryEx.Message}");
            }
        }
    }

    /// <summary>Strips an accidental code fence before deserializing (defensive against chatty models).</summary>
    private static AiSmartSetupOutput ParseOutput(string content)
    {
        var json = StripCodeFence(content);

        return JsonSerializer.Deserialize<AiSmartSetupOutput>(json, AiOutputJson)
               ?? throw new JsonException("AI trả về JSON rỗng (null).");
    }

    private static string StripCodeFence(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewline < 0 || lastFence <= firstNewline)
        {
            return trimmed;
        }

        return trimmed[(firstNewline + 1)..lastFence].Trim();
    }

    // ---- normalization + resolution (pure, verify-friendly) -----------------

    /// <summary>
    /// Steps 3 and 9–11 as a pure function set: no DB, no provider. Public so normalization and
    /// resolution can be verified directly without HTTP or a live model (Phase 3 §4.3).
    /// </summary>
    public static SmartSetupProposal BuildProposal(
        AiSmartSetupOutput output,
        IReadOnlyList<WorkspaceMemberResponse> members,
        IReadOnlyList<WorkspaceLabel> existingLabels,
        int maxTaskCount)
    {
        var tasks = NormalizeTasks(output.Tasks, members, existingLabels, maxTaskCount);
        if (tasks.Count == 0)
        {
            throw new BadRequestException(
                "AI không trả về sub-task hợp lệ nào — hãy mô tả chi tiết hơn và thử lại.");
        }

        return new SmartSetupProposal(TrimToNull(output.Summary), tasks);
    }

    /// <summary>Step 9–11 for one task set: drop unusable tasks, normalize, then resolve.</summary>
    public static List<SmartSetupTaskProposal> NormalizeTasks(
        IReadOnlyList<AiSmartSetupTask>? tasks,
        IReadOnlyList<WorkspaceMemberResponse> members,
        IReadOnlyList<WorkspaceLabel> existingLabels,
        int maxTaskCount)
    {
        var proposals = new List<SmartSetupTaskProposal>();
        if (tasks is null || maxTaskCount <= 0)
        {
            return proposals;
        }

        foreach (var task in tasks.Take(maxTaskCount))
        {
            // title 1–200 is mandatory; an unusable title drops the whole task.
            var title = TrimToNull(task.Title);
            if (title is null || title.Length > MaxTitleLength)
            {
                continue;
            }

            proposals.Add(new SmartSetupTaskProposal(
                title,
                CapLength(TrimToNull(task.Description), MaxTaskDescriptionLength),
                NormalizePriority(task.Priority),
                ResolveLabels(task.Labels, existingLabels),
                ResolveAssignee(task.SuggestedAssignee, members)));
        }

        return proposals;
    }

    /// <summary>Recognised priority → canonical casing; anything else (or blank) → null.</summary>
    public static string? NormalizePriority(string? priority)
    {
        var value = TrimToNull(priority);
        if (value is null)
        {
            return null;
        }

        return ValidPriorities.FirstOrDefault(
            p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Step 10. Three-tier resolution: exact display-name match → contains → (only when the AI
    /// named nobody and the workspace has exactly one member) that sole member.
    /// A name that matches nobody is kept with <c>Matched = false</c> so the UI can show what the
    /// AI intended and let the user re-pick.
    /// </summary>
    public static SmartSetupAssigneeSuggestion? ResolveAssignee(
        string? suggestedAssignee,
        IReadOnlyList<WorkspaceMemberResponse> members)
    {
        var name = TrimToNull(suggestedAssignee);

        if (members.Count == 0)
        {
            return name is null
                ? null
                : new SmartSetupAssigneeSuggestion(null, name, Matched: false);
        }

        if (name is null)
        {
            // No suggestion: with a single member there is no choice to make, so pre-select them.
            return members.Count == 1
                ? new SmartSetupAssigneeSuggestion(members[0].UserId, members[0].DisplayName, Matched: true)
                : null;
        }

        var exact = members.FirstOrDefault(m => string.Equals(m.DisplayName, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return new SmartSetupAssigneeSuggestion(exact.UserId, exact.DisplayName, Matched: true);
        }

        var contains = members.FirstOrDefault(
            m => m.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase)
                 || name.Contains(m.DisplayName, StringComparison.OrdinalIgnoreCase));

        return contains is not null
            ? new SmartSetupAssigneeSuggestion(contains.UserId, contains.DisplayName, Matched: true)
            : new SmartSetupAssigneeSuggestion(null, name, Matched: false);
    }

    /// <summary>
    /// Step 11. Labels are trimmed, blanks and over-long names dropped, deduplicated
    /// case-insensitively and capped. An existing workspace label resolves to its id
    /// (<c>Exists = true</c>); otherwise the name comes back as a new label suggestion.
    /// </summary>
    public static List<SmartSetupLabelSuggestion> ResolveLabels(
        IReadOnlyList<string>? labels,
        IReadOnlyList<WorkspaceLabel> existingLabels)
    {
        var result = new List<SmartSetupLabelSuggestion>();
        if (labels is null)
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in labels)
        {
            var name = TrimToNull(raw);
            if (name is null || name.Length > MaxLabelLength || !seen.Add(name))
            {
                continue;
            }

            var existing = existingLabels.FirstOrDefault(
                l => string.Equals(TrimToNull(l.Name), name, StringComparison.OrdinalIgnoreCase));

            result.Add(existing is null
                ? new SmartSetupLabelSuggestion(null, name, Exists: false)
                : new SmartSetupLabelSuggestion(existing.Id, name, Exists: true));

            if (result.Count == MaxLabelsPerTask)
            {
                break;
            }
        }

        return result;
    }

    // ---- small helpers ------------------------------------------------------

    private static string ValidateDescription(string? description)
    {
        var value = TrimToNull(description);
        if (value is null)
        {
            throw new BadRequestException(
                $"Description must be 1–{MaxDescriptionLength} characters.");
        }

        if (value.Length > MaxDescriptionLength)
        {
            throw new BadRequestException(
                $"Description must be 1–{MaxDescriptionLength} characters (received {value.Length}).");
        }

        return value;
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string? CapLength(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];
}

/// <summary>
/// Minimal label projection (id + name) used for resolution: the Ai module only needs ids and
/// names, so it does not depend on the Board module's full <c>LabelResponse</c>.
/// </summary>
public sealed record WorkspaceLabel(Guid Id, string Name);
