using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Contracts;
using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// The AI Board Template (Phase 14 §4, decision D12): one proposal that structures a whole board, and
/// one <c>Pending</c> action that creates it after human approval.
/// <para>
/// <b>Generate writes nothing and confirm writes only a log row.</b> That split is the same one Phase 3
/// established for sub-tasks, and it is what makes "xem trước rồi mới xác nhận" true rather than
/// decorative: the board really does not exist until a Manager approves.
/// </para>
/// </summary>
public interface IBoardTemplateService
{
    /// <summary>
    /// Builds a board + column + starter-task proposal for <paramref name="workspaceId"/>. Requires
    /// Manager/Admin (403), 404 for an unknown workspace, 400 on an unusable description or proposal.
    /// Never writes.
    /// </summary>
    Task<BoardTemplateProposal> GenerateAsync(
        Guid workspaceId,
        BoardTemplateRequest request,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Records a reviewed proposal as a <c>Pending</c> <c>CreateBoardFromTemplate</c> action scoped to
    /// the workspace (Manager/Admin). Re-validates everything client-side input could have broken.
    /// </summary>
    Task<AiActionLogResponse> ConfirmAsync(
        Guid workspaceId,
        ConfirmBoardTemplateRequest request,
        Guid userId,
        CancellationToken ct = default);
}

public sealed class BoardTemplateService : IBoardTemplateService
{
    /// <summary>camelCase for the snapshots, matching the Accountability Layer's convention.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Cap on the description kept inside <c>basis</c> (token/cost control, text only).</summary>
    public const int BasisExcerptLength = 300;

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IWorkspaceMemberService _members;
    private readonly IAiProvider _aiProvider;
    private readonly IAiActionService _actions;
    private readonly DeepSeekOptions _options;
    private readonly ILogger<BoardTemplateService> _logger;

    public BoardTemplateService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IWorkspaceMemberService members,
        IAiProvider aiProvider,
        IAiActionService actions,
        IOptions<DeepSeekOptions> options,
        ILogger<BoardTemplateService> logger)
    {
        _db = db;
        _access = access;
        _members = members;
        _aiProvider = aiProvider;
        _actions = actions;
        _options = options.Value;
        _logger = logger;
    }

    // ---- generate (never writes) ------------------------------------------

    public async Task<BoardTemplateProposal> GenerateAsync(
        Guid workspaceId,
        BoardTemplateRequest request,
        Guid userId,
        CancellationToken ct = default)
    {
        // 1) Workspace exists and the caller manages it (404 unknown, 403 plain Member).
        var workspace = await _db.Workspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workspaceId, ct)
            ?? throw new NotFoundException("Workspace not found.");

        await _access.RequireManagerAsync(workspaceId, userId, ct);

        // 2) Validate the description before spending any tokens.
        var description = ValidateDescription(request.Description);

        // 3) The vocabulary the proposal must stay inside: members (for assignees) and labels.
        var members = await _members.GetMembersAsync(workspaceId, userId, ct);
        var labels = await _db.Labels
            .Where(l => l.WorkspaceId == workspaceId)
            .OrderBy(l => l.Name)
            .Select(l => new WorkspaceLabel(l.Id, l.Name))
            .AsNoTracking()
            .ToListAsync(ct);

        var systemPrompt = BoardTemplatePrompts.BuildSystemPrompt();
        var userPrompt = BoardTemplatePrompts.BuildUserPrompt(
            string.IsNullOrWhiteSpace(workspace.Name) ? "(không tên)" : workspace.Name,
            // Only HUMANS are offered as assignees: suggesting the AI Agent for a starter task the team
            // lead did not ask for would be a surprise, and its tasks are triggered explicitly elsewhere.
            members.Where(m => !string.Equals(m.MemberType, "AiAgent", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.DisplayName)
                .ToList(),
            labels.Select(l => l.Name).ToList(),
            description);

        var output = await AiJsonCompletion.CompleteWithRetryAsync(
            _aiProvider,
            _options,
            systemPrompt,
            userPrompt,
            error => BoardTemplatePrompts.BuildRetryUserPrompt(userPrompt, error),
            ParseOutput,
            _logger,
            ct);

        var proposal = BoardTemplateValidator.Normalize(
            output, description, members, labels, _options.MaxTaskCount);

        _logger.LogInformation(
            "Board template proposal generated for workspace {WorkspaceId}: {Columns} column(s), "
            + "{Tasks} starter task(s).",
            workspaceId,
            proposal.Columns.Count,
            proposal.Tasks.Count);

        // In-memory only — Phase 14 never persists a proposal.
        return proposal;
    }

    // ---- confirm (Pending log only) ---------------------------------------

    public async Task<AiActionLogResponse> ConfirmAsync(
        Guid workspaceId,
        ConfirmBoardTemplateRequest request,
        Guid userId,
        CancellationToken ct = default)
    {
        var workspace = await _db.Workspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workspaceId, ct)
            ?? throw new NotFoundException("Workspace not found.");

        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var proposal = request.Proposal
            ?? throw new BadRequestException("Thiếu đề xuất bảng để xác nhận.");

        // The proposal made a round trip through the browser, so NOTHING about it is trusted any more:
        // it is re-shaped from scratch (columns re-pinned to exactly one done lane, tasks re-bounded).
        // A payload edited by hand must not be able to create a malformed board.
        var members = await _members.GetMembersAsync(workspaceId, userId, ct);
        var labels = await _db.Labels
            .Where(l => l.WorkspaceId == workspaceId)
            .OrderBy(l => l.Name)
            .Select(l => new WorkspaceLabel(l.Id, l.Name))
            .AsNoTracking()
            .ToListAsync(ct);

        var columns = BoardTemplateValidator.NormalizeColumns(
            proposal.Columns
                .Select(c => new AiBoardTemplateColumn { Name = c.Name, IsDone = c.IsDone })
                .ToList());

        if (columns.Count < BoardTemplatePrompts.MinColumns)
        {
            throw new BadRequestException(
                $"Đề xuất bảng phải có ít nhất {BoardTemplatePrompts.MinColumns} cột.");
        }

        var tasks = BoardTemplateValidator.NormalizeTasks(
            proposal.Tasks
                .Select(t => new AiBoardTemplateTask
                {
                    Title = t.Title,
                    Description = t.Description,
                    Priority = t.Priority,
                    ColumnName = t.ColumnName,
                    Labels = t.Labels.Select(l => l.Name).ToList(),
                    SuggestedAssignee = t.Assignee?.DisplayName,
                })
                .ToList(),
            columns,
            members,
            labels,
            Math.Min(_options.MaxTaskCount, BoardTemplatePrompts.MaxTasks));

        if (tasks.Count == 0)
        {
            throw new BadRequestException("Đề xuất bảng phải có ít nhất một task khởi đầu.");
        }

        var normalized = new BoardTemplateProposal(
            string.IsNullOrWhiteSpace(proposal.Summary) ? null : proposal.Summary.Trim(),
            BoardTemplateValidator.NormalizeBoardName(proposal.BoardName, proposal.BoardName ?? string.Empty),
            string.IsNullOrWhiteSpace(proposal.BoardDescription) ? null : proposal.BoardDescription.Trim(),
            columns,
            tasks);

        var afterSnapshot = JsonSerializer.Serialize(normalized, Json);
        var basis = JsonSerializer.Serialize(new
        {
            source = "BoardTemplate",
            workspaceId,
            workspaceName = workspace.Name,
            boardName = normalized.BoardName,
            columnCount = normalized.Columns.Count,
            taskCount = normalized.Tasks.Count,
            requestedAt = DateTimeOffset.UtcNow,
        }, Json);

        var log = await _actions.RequestCreateBoardFromTemplateAsync(
            workspaceId, userId, afterSnapshot, basis, ct);

        _logger.LogInformation(
            "Board template for workspace {WorkspaceId} recorded as Pending action {LogId} by user {UserId} "
            + "({Columns} column(s), {Tasks} task(s)).",
            workspaceId, log.Id, userId, normalized.Columns.Count, normalized.Tasks.Count);

        return log;
    }

    // ---- helpers ----------------------------------------------------------

    private static string ValidateDescription(string? description)
    {
        var value = description?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            throw new BadRequestException(
                $"Description must be 1–{SmartSetupService.MaxDescriptionLength} characters.");
        }

        if (value.Length > SmartSetupService.MaxDescriptionLength)
        {
            throw new BadRequestException(
                $"Description must be 1–{SmartSetupService.MaxDescriptionLength} characters (received {value.Length}).");
        }

        return value;
    }

    /// <summary>
    /// Tolerant AI JSON parse: an accidental code fence is stripped, then a failure bubbles as
    /// <see cref="JsonException"/> so <see cref="AiJsonCompletion"/> can run its single stricter retry.
    /// Visible to tests because the fence tolerance is a real behaviour, not an implementation detail.
    /// </summary>
    public static AiBoardTemplateOutput ParseOutput(string content)
        => JsonSerializer.Deserialize<AiBoardTemplateOutput>(StripCodeFence(content), Json)
           ?? throw new JsonException("AI trả về JSON rỗng (null).");

    /// <summary>Removes one surrounding ```…``` fence if the model wrapped its JSON in markdown.</summary>
    public static string StripCodeFence(string content)
    {
        var trimmed = content.Trim();

        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);

        return firstNewline < 0 || lastFence <= firstNewline
            ? trimmed
            : trimmed[(firstNewline + 1)..lastFence].Trim();
    }
}
