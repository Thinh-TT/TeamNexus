using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Board.Services.Integrations;

/// <summary>
/// Service implementing GitHub workspace integration management and connectivity testing.
/// Protects sensitive credentials (PAT, Webhook Secret) using <see cref="IIntegrationSecretProtector"/>
/// and verifies repository permissions before binding.
/// </summary>
public sealed class GitHubIntegrationService : IGitHubIntegrationService
{
    public const string ProviderName = "github";
    public const string DefaultWebhookPath = "/api/webhooks/github";

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IIntegrationSecretProtector _protector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubIntegrationService> _logger;

    public GitHubIntegrationService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IIntegrationSecretProtector protector,
        IHttpClientFactory httpClientFactory,
        ILogger<GitHubIntegrationService> logger)
    {
        _db = db;
        _access = access;
        _protector = protector;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<GitHubIntegrationResponse?> GetAsync(Guid workspaceId, Guid callerUserId, CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, callerUserId, ct);

        var entity = await _db.WorkspaceIntegrations
            .FirstOrDefaultAsync(wi => wi.WorkspaceId == workspaceId && wi.Provider == ProviderName, ct);

        return entity is null ? null : MapToResponse(entity);
    }

    public async Task<GitHubIntegrationResponse> UpsertAsync(
        Guid workspaceId,
        Guid callerUserId,
        UpdateGitHubIntegrationRequest request,
        CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, callerUserId, ct);

        if (request.RepoName != null)
        {
            var trimmedRepo = request.RepoName.Trim();
            if (!string.IsNullOrEmpty(trimmedRepo))
            {
                ValidateRepoName(trimmedRepo);
            }
        }

        var entity = await _db.WorkspaceIntegrations
            .FirstOrDefaultAsync(wi => wi.WorkspaceId == workspaceId && wi.Provider == ProviderName, ct);

        var isNew = false;
        if (entity is null)
        {
            isNew = true;
            entity = new WorkspaceIntegration
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                Provider = ProviderName,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                IsEnabled = true
            };
        }

        if (request.RepoName != null)
        {
            var trimmedRepo = request.RepoName.Trim();
            entity.RepoName = string.IsNullOrEmpty(trimmedRepo) ? null : trimmedRepo;
        }

        if (!string.IsNullOrWhiteSpace(request.PersonalAccessToken))
        {
            entity.EncryptedPat = _protector.Protect(request.PersonalAccessToken.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.WebhookSecret))
        {
            entity.EncryptedWebhookSecret = _protector.Protect(request.WebhookSecret.Trim());
        }

        if (request.IsEnabled.HasValue)
        {
            entity.IsEnabled = request.IsEnabled.Value;
        }

        entity.UpdatedAt = DateTimeOffset.UtcNow;

        if (isNew)
        {
            _db.WorkspaceIntegrations.Add(entity);
        }

        await _db.SaveChangesAsync(ct);

        return MapToResponse(entity);
    }

    public async Task DeleteAsync(Guid workspaceId, Guid callerUserId, CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, callerUserId, ct);

        var entity = await _db.WorkspaceIntegrations
            .FirstOrDefaultAsync(wi => wi.WorkspaceId == workspaceId && wi.Provider == ProviderName, ct);

        if (entity is not null)
        {
            _db.WorkspaceIntegrations.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<TestGitHubConnectionResponse> TestConnectionAsync(
        Guid workspaceId,
        Guid callerUserId,
        CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, callerUserId, ct);

        var entity = await _db.WorkspaceIntegrations
            .FirstOrDefaultAsync(wi => wi.WorkspaceId == workspaceId && wi.Provider == ProviderName, ct);

        if (entity is null || string.IsNullOrWhiteSpace(entity.EncryptedPat) || string.IsNullOrWhiteSpace(entity.RepoName))
        {
            return new TestGitHubConnectionResponse(
                Success: false,
                RepoFullName: null,
                DefaultBranch: null,
                ErrorMessage: "Chưa cấu hình Repository hoặc Personal Access Token.");
        }

        string pat;
        try
        {
            pat = _protector.Unprotect(entity.EncryptedPat);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt Personal Access Token for workspace {WorkspaceId}", workspaceId);
            return new TestGitHubConnectionResponse(
                Success: false,
                RepoFullName: null,
                DefaultBranch: null,
                ErrorMessage: "Không thể giải mã Personal Access Token đã lưu.");
        }

        if (string.IsNullOrWhiteSpace(pat))
        {
            return new TestGitHubConnectionResponse(
                Success: false,
                RepoFullName: null,
                DefaultBranch: null,
                ErrorMessage: "Chưa cấu hình Repository hoặc Personal Access Token.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient("GitHubIntegration");
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{entity.RepoName}");
            request.Headers.UserAgent.ParseAdd("TeamNexus-App");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pat);
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await client.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = doc.RootElement;

                var fullName = root.TryGetProperty("full_name", out var fnProp) ? fnProp.GetString() : entity.RepoName;
                var defaultBranch = root.TryGetProperty("default_branch", out var dbProp) ? dbProp.GetString() : "main";

                return new TestGitHubConnectionResponse(
                    Success: true,
                    RepoFullName: fullName,
                    DefaultBranch: defaultBranch,
                    ErrorMessage: null);
            }

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            string reason = response.ReasonPhrase ?? response.StatusCode.ToString();
            if (!string.IsNullOrWhiteSpace(errorBody))
            {
                try
                {
                    using var errDoc = JsonDocument.Parse(errorBody);
                    if (errDoc.RootElement.TryGetProperty("message", out var msgProp) && !string.IsNullOrWhiteSpace(msgProp.GetString()))
                    {
                        reason = msgProp.GetString()!;
                    }
                }
                catch
                {
                    // Non-JSON error body fallback
                }
            }

            return new TestGitHubConnectionResponse(
                Success: false,
                RepoFullName: null,
                DefaultBranch: null,
                ErrorMessage: $"GitHub API returned {response.StatusCode}: {reason}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error testing GitHub connection for repo {RepoName}", entity.RepoName);
            return new TestGitHubConnectionResponse(
                Success: false,
                RepoFullName: null,
                DefaultBranch: null,
                ErrorMessage: $"Lỗi kết nối GitHub: {ex.Message}");
        }
    }

    public static void ValidateRepoName(string repoName)
    {
        var parts = repoName.Split('/');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new BadRequestException("Tên repository phải có định dạng owner/repo (ví dụ: facebook/react).");
        }
    }

    public static string? MaskPat(string? pat)
    {
        if (string.IsNullOrWhiteSpace(pat))
        {
            return null;
        }

        var trimmed = pat.Trim();
        if (trimmed.Length <= 4)
        {
            return new string('•', trimmed.Length);
        }

        var last4 = trimmed[^4..];
        if (trimmed.StartsWith("ghp_", StringComparison.OrdinalIgnoreCase))
        {
            return $"ghp_••••••••{last4}";
        }

        if (trimmed.StartsWith("github_pat_", StringComparison.OrdinalIgnoreCase))
        {
            return $"github_pat_••••••••{last4}";
        }

        return $"••••{last4}";
    }

    private GitHubIntegrationResponse MapToResponse(WorkspaceIntegration entity)
    {
        var hasPat = !string.IsNullOrEmpty(entity.EncryptedPat);
        var hasWebhookSecret = !string.IsNullOrEmpty(entity.EncryptedWebhookSecret);

        string? maskedPat = null;
        if (hasPat)
        {
            try
            {
                var plaintext = _protector.Unprotect(entity.EncryptedPat);
                maskedPat = MaskPat(plaintext);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unprotect PAT for workspace {WorkspaceId}", entity.WorkspaceId);
                maskedPat = "••••••••";
            }
        }

        return new GitHubIntegrationResponse(
            WorkspaceId: entity.WorkspaceId,
            Provider: entity.Provider,
            RepoName: entity.RepoName,
            HasPat: hasPat,
            MaskedPat: maskedPat,
            HasWebhookSecret: hasWebhookSecret,
            IsEnabled: entity.IsEnabled,
            WebhookUrl: DefaultWebhookPath,
            UpdatedAt: entity.UpdatedAt);
    }
}
