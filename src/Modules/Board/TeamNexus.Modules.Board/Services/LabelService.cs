using Microsoft.EntityFrameworkCore;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;
using BoardEntity = TeamNexus.Persistence.Data.Entities.Board;

namespace TeamNexus.Modules.Board.Services;

public interface ILabelService
{
    Task<IReadOnlyList<LabelResponse>> GetLabelsAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);

    Task<LabelResponse> CreateLabelAsync(Guid workspaceId, CreateLabelRequest request, Guid userId, CancellationToken ct = default);

    Task DeleteLabelAsync(Guid workspaceId, Guid labelId, Guid userId, CancellationToken ct = default);

    Task AttachToTaskAsync(Guid taskId, AttachLabelRequest request, Guid userId, CancellationToken ct = default);

    Task DetachFromTaskAsync(Guid taskId, Guid labelId, Guid userId, CancellationToken ct = default);
}

public sealed class LabelService : ILabelService
{
    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;

    public LabelService(TeamNexusDbContext db, IWorkspaceAccess access)
    {
        _db = db;
        _access = access;
    }

    public async Task<IReadOnlyList<LabelResponse>> GetLabelsAsync(
        Guid workspaceId, Guid userId, CancellationToken ct = default)
    {
        await _access.RequireMemberAsync(workspaceId, userId, ct);

        var labels = await _db.Labels
            .Where(l => l.WorkspaceId == workspaceId)
            .OrderBy(l => l.Name)
            .AsNoTracking()
            .ToListAsync(ct);

        return labels.Select(DtoMapping.MapLabel).ToList();
    }

    public async Task<LabelResponse> CreateLabelAsync(
        Guid workspaceId, CreateLabelRequest request, Guid userId, CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, userId, ct);
        Validate(request.Name, request.Color);

        var exists = await _db.Labels.AnyAsync(
            l => l.WorkspaceId == workspaceId && l.Name == request.Name.Trim(), ct);

        if (exists)
        {
            throw new ConflictException("A label with this name already exists in the workspace.");
        }

        var label = new Label
        {
            WorkspaceId = workspaceId,
            Name = request.Name.Trim(),
            Color = request.Color.Trim().ToUpperInvariant(),
        };

        _db.Labels.Add(label);
        await _db.SaveChangesAsync(ct);

        return DtoMapping.MapLabel(label);
    }

    public async Task DeleteLabelAsync(
        Guid workspaceId, Guid labelId, Guid userId, CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var label = await _db.Labels
            .FirstOrDefaultAsync(l => l.Id == labelId && l.WorkspaceId == workspaceId, ct)
            ?? throw new NotFoundException("Label not found.");

        // Junction rows are Restrict-FK'd: remove the links before deleting the label.
        var links = await _db.TaskLabels
            .IgnoreQueryFilters()
            .Where(tl => tl.LabelId == labelId)
            .ToListAsync(ct);

        _db.TaskLabels.RemoveRange(links);
        _db.Labels.Remove(label);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AttachToTaskAsync(
        Guid taskId, AttachLabelRequest request, Guid userId, CancellationToken ct = default)
    {
        var (task, board) = await LoadTaskAndBoardAsync(taskId, ct);
        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);

        var label = await _db.Labels
            .FirstOrDefaultAsync(l => l.Id == request.LabelId, ct)
            ?? throw new NotFoundException("Label not found.");

        if (label.WorkspaceId != board.WorkspaceId)
        {
            throw new BadRequestException("Label belongs to a different workspace.");
        }

        var alreadyLinked = await _db.TaskLabels.AnyAsync(
            tl => tl.TaskId == taskId && tl.LabelId == label.Id, ct);

        if (!alreadyLinked)
        {
            _db.TaskLabels.Add(new TaskLabel { TaskId = taskId, LabelId = label.Id });
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task DetachFromTaskAsync(
        Guid taskId, Guid labelId, Guid userId, CancellationToken ct = default)
    {
        var (task, board) = await LoadTaskAndBoardAsync(taskId, ct);
        await _access.RequireMemberAsync(board.WorkspaceId, userId, ct);

        var link = await _db.TaskLabels
            .FirstOrDefaultAsync(tl => tl.TaskId == taskId && tl.LabelId == labelId, ct);

        if (link is not null)
        {
            _db.TaskLabels.Remove(link);
            await _db.SaveChangesAsync(ct);
        }
    }

    // ---- helpers ----------------------------------------------------------

    private async Task<(BoardTask Task, BoardEntity Board)> LoadTaskAndBoardAsync(Guid taskId, CancellationToken ct)
    {
        var task = await _db.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Task not found.");

        var board = await _db.Boards
            .FirstOrDefaultAsync(b => b.Id == task.BoardId, ct)
            ?? throw new NotFoundException("Task not found.");

        return (task, board);
    }

    private static void Validate(string name, string color)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
        {
            throw new BadRequestException("Label name must be 1–80 characters.");
        }

        // Hex color: #RGB #RRGGBB or #RRGGBBAA.
        var hex = color?.Trim() ?? string.Empty;
        var valid = hex.Length is 4 or 7 or 9
                    && hex[0] == '#'
                    && hex[1..].All(Uri.IsHexDigit);

        if (!valid)
        {
            throw new BadRequestException("Color must be a hex code (#RGB, #RRGGBB or #RRGGBBAA).");
        }
    }
}
