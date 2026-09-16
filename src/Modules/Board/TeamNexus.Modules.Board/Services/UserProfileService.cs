using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;
using TeamNexus.Shared.Profile;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// The signed-in user's own profile (Phase 11 §5.3): read/update the two editable fields, list the
/// workspaces they belong to, and let them leave one.
/// <para>
/// Every operation is scoped to the caller — no route takes a user id — so there is no way to read or
/// edit somebody else's profile through this surface.
/// </para>
/// </summary>
public interface IUserProfileService
{
    Task<UserProfileResponse> GetAsync(Guid userId, CancellationToken ct = default);

    Task<UserProfileResponse> UpdateAsync(
        UpdateProfileRequest request, Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<MyWorkspaceResponse>> ListMyWorkspacesAsync(
        Guid userId, CancellationToken ct = default);

    Task LeaveWorkspaceAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);
}

public sealed class UserProfileService : IUserProfileService
{
    /// <summary>Keeps the display name in the same band as the other human-facing names.</summary>
    public const int MaxDisplayNameLength = 120;

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IActivityLogWriter _activityLog;

    public UserProfileService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IActivityLogWriter activityLog)
    {
        _db = db;
        _access = access;
        _activityLog = activityLog;
    }

    public async Task<UserProfileResponse> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await LoadUserAsync(userId, ct);
        return ToResponse(user);
    }

    public async Task<UserProfileResponse> UpdateAsync(
        UpdateProfileRequest request, Guid userId, CancellationToken ct = default)
    {
        var user = await LoadUserAsync(userId, ct);

        var displayName = (request.DisplayName ?? string.Empty).Trim();
        if (displayName.Length == 0)
        {
            throw new BadRequestException("Tên hiển thị không được để trống.");
        }

        if (displayName.Length > MaxDisplayNameLength)
        {
            throw new BadRequestException($"Tên hiển thị tối đa {MaxDisplayNameLength} ký tự.");
        }

        var avatarUrl = AvatarUrl.Normalize(request.AvatarUrl);

        // An unusable URL is reported, never silently discarded: dropping it would leave the user
        // believing the update succeeded while their avatar silently reverted.
        if (!AvatarUrl.IsSafe(avatarUrl))
        {
            throw new BadRequestException(
                "URL ảnh đại diện không hợp lệ. Chỉ chấp nhận liên kết http hoặc https.");
        }

        // `users` has no `updated_at` and ApplicationUser does not implement IAuditableEntity
        // (Phase 1 schema), so exactly these columns change — the audit stamping convention
        // simply does not apply to this table.
        user.DisplayName = displayName;
        user.AvatarUrl = avatarUrl;

        // Phase 13 §3.4: an ABSENT value means "not mentioned", not "off". A client that predates the
        // digest feature sends only displayName/avatarUrl, and defaulting the missing field to false here
        // would silently unsubscribe every user the moment they edited their display name. `false` only
        // ever arrives as an explicit instruction.
        if (request.DigestEnabled is { } digestEnabled)
        {
            user.DigestEnabled = digestEnabled;
        }

        await _db.SaveChangesAsync(ct);

        return ToResponse(user);
    }

    public async Task<IReadOnlyList<MyWorkspaceResponse>> ListMyWorkspacesAsync(
        Guid userId, CancellationToken ct = default)
    {
        // Read-only on purpose: WorkspaceService.ListForUserAsync CREATES a default workspace when
        // the list is empty. The dashboard relies on that side effect, but "show me my workspaces"
        // must not write anything — so the query is written out here instead of delegating.
        return await _db.WorkspaceMembers
            .AsNoTracking()
            .Where(wm => wm.UserId == userId && wm.Workspace != null)
            .OrderBy(wm => wm.JoinedAt)
            .Select(wm => new MyWorkspaceResponse(
                wm.Workspace!.Id,
                wm.Workspace.Name,
                wm.Workspace.Description,
                wm.Role.ToString(),
                wm.Workspace.OwnerId,
                wm.Workspace.OwnerId == userId))
            .ToListAsync(ct);
    }

    public async Task LeaveWorkspaceAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
    {
        // 404 when the workspace is invisible to the caller — leaving something you are not in is
        // indistinguishable from it not existing.
        var membership = await _access.RequireMemberAsync(workspaceId, userId, ct);

        var workspace = await _db.Workspaces.FirstAsync(w => w.Id == workspaceId, ct);

        if (workspace.OwnerId == userId)
        {
            throw new BadRequestException(
                "Chủ sở hữu không thể rời workspace. Hãy chuyển quyền sở hữu cho người khác trước.");
        }

        if (membership.Role == WorkspaceRole.Admin)
        {
            var admins = await _db.WorkspaceMembers
                .CountAsync(wm => wm.WorkspaceId == workspaceId && wm.Role == WorkspaceRole.Admin, ct);

            if (admins <= 1)
            {
                throw new BadRequestException("Workspace phải còn ít nhất một Admin.");
            }
        }

        _db.WorkspaceMembers.Remove(membership);
        await _db.SaveChangesAsync(ct);

        await _activityLog.RecordAsync(new ActivityLogEntry(
            workspaceId,
            BoardId: null,
            userId,
            ObserverEntityTypes.Workspace,
            userId,
            ObserverActivityActions.MemberLeft,
            JsonSerializer.Serialize(new { memberUserId = userId }, ActivityJson)),
            ct);
    }

    // ---- helpers -----------------------------------------------------------

    /// <summary>camelCase JSON for activity payloads (Phase 5 §2, decision A6).</summary>
    private static readonly JsonSerializerOptions ActivityJson = new(JsonSerializerDefaults.Web);

    private async Task<ApplicationUser> LoadUserAsync(Guid userId, CancellationToken ct)
        => await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
           ?? throw new NotFoundException("Không tìm thấy tài khoản.");

    private static UserProfileResponse ToResponse(ApplicationUser user)
        => new(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.AvatarUrl,
            user.CreatedAt,
            user.DigestEnabled);
}
