using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TeamNexus.Modules.Ai.DTOs.Integrations;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// Implementation of <see cref="IWorkspaceGitHubIntegrationService"/> (Phase 15.3 §T15.3.2).
/// </summary>
public sealed class WorkspaceGitHubIntegrationService : IWorkspaceGitHubIntegrationService
{
    private const string DefaultApiBaseUrl = "https://api.teamnexus.cloud";

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IGitHubSecretProtector _protector;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _clock;

    public WorkspaceGitHubIntegrationService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IGitHubSecretProtector protector,
        IConfiguration configuration,
        TimeProvider clock)
    {
        _db = db;
        _access = access;
        _protector = protector;
        _configuration = configuration;
        _clock = clock;
    }

    public async Task<WorkspaceGitHubIntegrationResponse?> GetAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var entity = await _db.WorkspaceGitHubIntegrations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);

        if (entity is null)
        {
            return null;
        }

        return MapResponse(entity, secretPlaintext: null);
    }

    public async Task<WorkspaceGitHubIntegrationResponse> UpsertAsync(
        Guid workspaceId,
        UpsertWorkspaceGitHubIntegrationRequest request,
        Guid userId,
        CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        if (request.DefaultBoardId.HasValue && request.DefaultBoardId.Value != Guid.Empty)
        {
            var boardExists = await _db.Boards
                .AsNoTracking()
                .AnyAsync(b => b.Id == request.DefaultBoardId.Value && b.WorkspaceId == workspaceId && b.DeletedAt == null, ct);

            if (!boardExists)
            {
                throw new BadRequestException("Bảng mặc định không tồn tại hoặc không thuộc Không gian làm việc này.");
            }
        }

        var entity = await _db.WorkspaceGitHubIntegrations
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);

        var now = _clock.GetUtcNow();
        string? plainSecretOnce = null;

        if (entity is null)
        {
            plainSecretOnce = _protector.GenerateNewSecret();
            var ciphertext = _protector.Protect(plainSecretOnce);
            var hint = _protector.GetHint(plainSecretOnce);

            entity = new WorkspaceGitHubIntegration
            {
                WorkspaceId = workspaceId,
                WebhookToken = Guid.NewGuid(),
                SecretCiphertext = ciphertext,
                SecretHint = hint,
                IsEnabled = request.IsEnabled ?? true,
                DefaultBoardId = request.DefaultBoardId == Guid.Empty ? null : request.DefaultBoardId,
                IssueLabelFilter = string.IsNullOrWhiteSpace(request.IssueLabelFilter) ? null : request.IssueLabelFilter.Trim(),
                AutoMoveOnPr = request.AutoMoveOnPr ?? true,
                AutoCreateTasksFromIssues = request.AutoCreateTasksFromIssues ?? true,
                SyncIssueComments = request.SyncIssueComments ?? true,
                CreatedByUserId = userId,
                CreatedAt = now,
                UpdatedAt = now,
            };

            _db.WorkspaceGitHubIntegrations.Add(entity);
        }
        else
        {
            if (request.IsEnabled.HasValue)
            {
                entity.IsEnabled = request.IsEnabled.Value;
            }

            if (request.DefaultBoardId.HasValue)
            {
                entity.DefaultBoardId = request.DefaultBoardId.Value == Guid.Empty ? null : request.DefaultBoardId.Value;
            }

            if (request.IssueLabelFilter is not null)
            {
                entity.IssueLabelFilter = string.IsNullOrWhiteSpace(request.IssueLabelFilter) ? null : request.IssueLabelFilter.Trim();
            }

            if (request.AutoMoveOnPr.HasValue)
            {
                entity.AutoMoveOnPr = request.AutoMoveOnPr.Value;
            }

            if (request.AutoCreateTasksFromIssues.HasValue)
            {
                entity.AutoCreateTasksFromIssues = request.AutoCreateTasksFromIssues.Value;
            }

            if (request.SyncIssueComments.HasValue)
            {
                entity.SyncIssueComments = request.SyncIssueComments.Value;
            }

            entity.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);

        return MapResponse(entity, plainSecretOnce);
    }

    public async Task<RotateGitHubSecretResponse> RotateSecretAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var entity = await _db.WorkspaceGitHubIntegrations
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct)
            ?? throw new NotFoundException("Tích hợp GitHub chưa được cấu hình cho Không gian làm việc này.");

        var newPlainSecret = _protector.GenerateNewSecret();
        entity.SecretCiphertext = _protector.Protect(newPlainSecret);
        entity.SecretHint = _protector.GetHint(newPlainSecret);
        entity.UpdatedAt = _clock.GetUtcNow();

        await _db.SaveChangesAsync(ct);

        return new RotateGitHubSecretResponse(entity.SecretHint, newPlainSecret);
    }

    public async Task DeleteAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var entity = await _db.WorkspaceGitHubIntegrations
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);

        if (entity is not null)
        {
            _db.WorkspaceGitHubIntegrations.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<GitHubWebhookDeliveryResponse>> GetDeliveriesAsync(
        Guid workspaceId,
        Guid userId,
        int take = 30,
        CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var clampedTake = Math.Clamp(take <= 0 ? 30 : take, 1, 100);

        var deliveries = await _db.GitHubWebhookDeliveries
            .AsNoTracking()
            .Where(d => d.WorkspaceId == workspaceId)
            .OrderByDescending(d => d.ReceivedAt)
            .Take(clampedTake)
            .ToListAsync(ct);

        return deliveries.Select(d => new GitHubWebhookDeliveryResponse(
            d.Id,
            d.DeliveryId,
            d.EventName,
            d.Action,
            d.RepositoryFullName,
            d.Status,
            d.Summary,
            d.ErrorMessage,
            d.ReceivedAt)).ToList();
    }

    private WorkspaceGitHubIntegrationResponse MapResponse(
        WorkspaceGitHubIntegration entity,
        string? secretPlaintext)
    {
        var apiBase = _configuration["Integrations:ApiBaseUrl"]?.TrimEnd('/')
                      ?? _configuration["Frontend:BaseUrl"]?.TrimEnd('/')
                      ?? DefaultApiBaseUrl;

        var payloadUrl = $"{apiBase}/api/webhooks/github/{entity.WebhookToken}";

        return new WorkspaceGitHubIntegrationResponse(
            entity.Id,
            entity.WorkspaceId,
            entity.WebhookToken,
            payloadUrl,
            entity.SecretHint,
            entity.IsEnabled,
            entity.DefaultBoardId,
            entity.IssueLabelFilter,
            entity.AutoMoveOnPr,
            entity.AutoCreateTasksFromIssues,
            entity.SyncIssueComments,
            entity.CreatedAt,
            entity.UpdatedAt,
            secretPlaintext);
    }
}
