using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Workspace invitations (Phase 11 §3.2): create a <c>Pending</c> row and hand the accept link to
/// the email port; list/cancel them; preview and accept a token.
/// <para>
/// The service owns the whole lifecycle. It never returns or logs the raw token — that value exists
/// exactly once, in the argument passed to <see cref="IEmailGateway"/>, and only its SHA-256 hash is
/// persisted.
/// </para>
/// </summary>
public interface IInvitationService
{
    /// <summary>Pending + historical invitations of the workspace, newest first (Manager+).</summary>
    Task<IReadOnlyList<InvitationResponse>> ListAsync(
        Guid workspaceId, Guid userId, CancellationToken ct = default);

    /// <summary>Creates the invitation and sends the accept email (Manager+).</summary>
    Task<InvitationResponse> CreateAsync(
        Guid workspaceId, CreateInvitationRequest request, Guid userId, CancellationToken ct = default);

    /// <summary>Cancels a <c>Pending</c> invitation of this workspace (Manager+).</summary>
    Task CancelAsync(
        Guid workspaceId, Guid invitationId, Guid userId, CancellationToken ct = default);

    /// <summary>Anonymous preview of an accept token, so the page can render before sign-in.</summary>
    Task<InvitationPreviewResponse> PreviewAsync(string token, CancellationToken ct = default);

    /// <summary>Accepts the token for the signed-in caller (email must match the invitation).</summary>
    Task<AcceptInvitationResponse> AcceptAsync(
        string token, Guid userId, string? userEmail, CancellationToken ct = default);
}

public sealed class InvitationService : IInvitationService
{
    /// <summary>SHA-256 hash of the raw token (mirrors <c>JwtService.HashToken</c>).</summary>
    public const int RawTokenBytes = 64;

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IEmailGateway _email;
    private readonly IActivityLogWriter _activityLog;
    private readonly InvitationSettings _settings;
    private readonly ILogger<InvitationService> _logger;

    public InvitationService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IEmailGateway email,
        IActivityLogWriter activityLog,
        InvitationSettings settings,
        ILogger<InvitationService> logger)
    {
        _db = db;
        _access = access;
        _email = email;
        _activityLog = activityLog;
        _settings = settings;
        _logger = logger;
    }

    public async Task<IReadOnlyList<InvitationResponse>> ListAsync(
        Guid workspaceId, Guid userId, CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        await ExpireStaleAsync(workspaceId, ct);

        var rows = await _db.WorkspaceInvitations
            .AsNoTracking()
            .Where(i => i.WorkspaceId == workspaceId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);

        var inviterIds = rows.Select(i => i.InvitedByUserId).Distinct().ToList();
        var inviterNames = await _db.Users
            .AsNoTracking()
            .Where(u => inviterIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return rows.Select(row => ToResponse(
            row,
            inviterNames.TryGetValue(row.InvitedByUserId, out var name) ? name : UnknownInviter,
            // Listing never re-sends anything: `emailSent` describes the send that happened at
            // creation time, which the list response cannot reconstruct.
            emailSent: true)).ToList();
    }

    public async Task<InvitationResponse> CreateAsync(
        Guid workspaceId, CreateInvitationRequest request, Guid userId, CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var email = NormalizeEmail(request.Email);
        if (!IsValidEmail(email))
        {
            throw new BadRequestException("Địa chỉ email không hợp lệ.");
        }

        var role = ParseRole(request.Role);

        // Re-inviting an existing member is a no-op the inviter should be told about, not a row
        // that can never be accepted.
        var alreadyMember = await _db.WorkspaceMembers
            .AnyAsync(wm => wm.WorkspaceId == workspaceId && wm.User != null
                            && wm.User.Email != null && wm.User.Email.ToLower() == email, ct);
        if (alreadyMember)
        {
            throw new BadRequestException("Người này đã là thành viên của workspace.");
        }

        await ExpireStaleAsync(workspaceId, ct);

        // The partial unique index (workspace_id, invited_email) WHERE status = 'Pending' is the
        // real guard; checking first turns a database error into a domain 409.
        var pendingExists = await _db.WorkspaceInvitations
            .AnyAsync(i => i.WorkspaceId == workspaceId
                           && i.InvitedEmail == email
                           && i.Status == InvitationStatus.Pending, ct);
        if (pendingExists)
        {
            throw new ConflictException("Đã có lời mời đang chờ cho email này.");
        }

        var rawToken = GenerateRawToken();
        var now = DateTimeOffset.UtcNow;

        var invitation = new WorkspaceInvitation
        {
            WorkspaceId = workspaceId,
            InvitedEmail = email,
            InvitedRole = role,
            TokenHash = HashToken(rawToken),
            InvitedByUserId = userId,
            Status = InvitationStatus.Pending,
            ExpiresAt = now.Add(_settings.InvitationLifetime),
        };

        _db.WorkspaceInvitations.Add(invitation);
        await _db.SaveChangesAsync(ct);

        var workspaceName = await _db.Workspaces
            .Where(w => w.Id == workspaceId)
            .Select(w => w.Name)
            .FirstOrDefaultAsync(ct) ?? "workspace";

        var inviterName = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct) ?? UnknownInviter;

        var acceptUrl = BuildAcceptUrl(rawToken);

        // Best-effort: a provider failure must not undo a committed invitation. The row is real
        // business data; the email is only the channel that announces it (the UI offers "Gửi lại").
        var emailSent = await _email.SendInvitationAsync(
            workspaceId,
            userId,
            email,
            workspaceName,
            inviterName,
            role.ToString(),
            acceptUrl,
            _settings.InvitationExpiryDays,
            ct);

        await _activityLog.RecordAsync(new ActivityLogEntry(
            workspaceId,
            BoardId: null,
            userId,
            ObserverEntityTypes.Workspace,
            invitation.Id,
            ObserverActivityActions.InvitationCreated,
            JsonSerializer.Serialize(new { invitationId = invitation.Id, role = role.ToString() })),
            ct);

        return ToResponse(invitation, inviterName, emailSent);
    }

    public async Task CancelAsync(
        Guid workspaceId, Guid invitationId, Guid userId, CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var invitation = await _db.WorkspaceInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.WorkspaceId == workspaceId, ct)
            ?? throw new NotFoundException("Invitation not found.");

        switch (invitation.Status)
        {
            case InvitationStatus.Cancelled:
                return; // idempotent

            case InvitationStatus.Accepted:
                throw new ConflictException("Lời mời này đã được chấp nhận, không thể huỷ.");

            case InvitationStatus.Expired:
                // Nothing left to cancel: normalise the state and finish.
                invitation.Status = InvitationStatus.Expired;
                await _db.SaveChangesAsync(ct);
                return;

            default:
                invitation.Status = InvitationStatus.Cancelled;
                await _db.SaveChangesAsync(ct);

                await _activityLog.RecordAsync(new ActivityLogEntry(
                    workspaceId,
                    BoardId: null,
                    userId,
                    ObserverEntityTypes.Workspace,
                    invitation.Id,
                    ObserverActivityActions.InvitationCancelled,
                    JsonSerializer.Serialize(new { invitationId = invitation.Id })),
                    ct);
                return;
        }
    }

    public async Task<InvitationPreviewResponse> PreviewAsync(string token, CancellationToken ct = default)
    {
        var invitation = await FindByTokenAsync(token, ct)
            ?? throw new NotFoundException("Lời mời không tồn tại hoặc đã bị xoá.");

        await EnsureNotExpiredAsync(invitation, ct);

        var workspaceName = await _db.Workspaces
            .Where(w => w.Id == invitation.WorkspaceId)
            .Select(w => w.Name)
            .FirstOrDefaultAsync(ct) ?? "workspace";

        var inviterName = await _db.Users
            .Where(u => u.Id == invitation.InvitedByUserId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct) ?? UnknownInviter;

        return new InvitationPreviewResponse(
            invitation.WorkspaceId,
            workspaceName,
            invitation.InvitedEmail,
            invitation.InvitedRole.ToString(),
            inviterName,
            invitation.ExpiresAt,
            invitation.Status.ToString());
    }

    public async Task<AcceptInvitationResponse> AcceptAsync(
        string token, Guid userId, string? userEmail, CancellationToken ct = default)
    {
        var invitation = await FindByTokenAsync(token, ct)
            ?? throw new NotFoundException("Lời mời không tồn tại hoặc đã bị xoá.");

        await EnsureNotExpiredAsync(invitation, ct);

        // The link is not a bearer credential for joining: only the person it was addressed to may
        // use it, otherwise a forwarded email would be an anonymous way into the workspace.
        var email = NormalizeEmail(userEmail);
        if (string.IsNullOrEmpty(email) || !string.Equals(email, invitation.InvitedEmail, StringComparison.Ordinal))
        {
            throw new ForbiddenException(
                "Lời mời này được gửi tới một email khác. Hãy đăng nhập bằng đúng email được mời.");
        }

        var existing = await _db.WorkspaceMembers
            .FirstOrDefaultAsync(wm => wm.WorkspaceId == invitation.WorkspaceId && wm.UserId == userId, ct);

        // Accepting twice is not an error — the invitation is already satisfied and the caller's
        // existing role must NOT be overwritten by invited_role (that would be a silent promotion).
        if (existing is not null)
        {
            invitation.Status = InvitationStatus.Accepted;
            invitation.AcceptedByUserId = userId;
            invitation.AcceptedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            return new AcceptInvitationResponse(invitation.WorkspaceId, existing.Role.ToString(), AlreadyMember: true);
        }

        var membership = new WorkspaceMember
        {
            WorkspaceId = invitation.WorkspaceId,
            UserId = userId,
            Role = invitation.InvitedRole,
            MemberType = MemberType.Human,
            JoinedAt = DateTimeOffset.UtcNow,
        };

        _db.WorkspaceMembers.Add(membership);
        invitation.Status = InvitationStatus.Accepted;
        invitation.AcceptedByUserId = userId;
        invitation.AcceptedAt = DateTimeOffset.UtcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Two concurrent accepts: the composite PK (workspace_id, user_id) is the physical
            // guard. The membership exists, so the invitation is still satisfied — report success
            // instead of leaving it stuck in Pending.
            _logger.LogWarning(ex, "Accept đua nhau cho workspace {WorkspaceId}; membership đã tồn tại.", invitation.WorkspaceId);

            _db.Entry(membership).State = EntityState.Detached;
            var current = await _db.WorkspaceMembers
                .AsNoTracking()
                .FirstOrDefaultAsync(wm => wm.WorkspaceId == invitation.WorkspaceId && wm.UserId == userId, ct)
                ?? throw new ConflictException("Bạn đã là thành viên của workspace này.");

            await _db.WorkspaceInvitations
                .Where(i => i.Id == invitation.Id && i.Status == InvitationStatus.Pending)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(i => i.Status, InvitationStatus.Accepted)
                        .SetProperty(i => i.AcceptedByUserId, userId)
                        .SetProperty(i => i.AcceptedAt, DateTimeOffset.UtcNow),
                    ct);

            return new AcceptInvitationResponse(invitation.WorkspaceId, current.Role.ToString(), AlreadyMember: true);
        }

        await _activityLog.RecordAsync(new ActivityLogEntry(
            invitation.WorkspaceId,
            BoardId: null,
            userId,
            ObserverEntityTypes.Workspace,
            invitation.Id,
            ObserverActivityActions.InvitationAccepted,
            JsonSerializer.Serialize(new { invitationId = invitation.Id, role = invitation.InvitedRole.ToString() })),
            ct);

        return new AcceptInvitationResponse(invitation.WorkspaceId, invitation.InvitedRole.ToString(), AlreadyMember: false);
    }

    // ---- helpers -----------------------------------------------------------

    private const string UnknownInviter = "Không xác định";

    /// <summary>Trim + lowercase, so the partial unique index and the accept-time check agree.</summary>
    public static string NormalizeEmail(string? email)
        => (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Deliberately permissive RFC-lite check (exactly one '@', text on both sides, a dotted domain,
    /// no whitespace, ≤ 320 chars) — matches the frontend rule and the varchar(320) column. Full
    /// RFC 5322 validation is not the job of this endpoint; the email must simply be deliverable.
    /// </summary>
    public static bool IsValidEmail(string email)
    {
        if (string.IsNullOrEmpty(email) || email.Length > 320)
        {
            return false;
        }

        var parts = email.Split('@');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            return false;
        }

        if (email.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var domain = parts[1];
        return domain.Contains('.')
               && !domain.StartsWith('.')
               && !domain.EndsWith('.')
               && domain.Length >= 3;
    }

    /// <summary>
    /// Strict role parse. <c>Enum.TryParse</c> alone is NOT a validator — it silently maps numeric
    /// strings by index ("1" ⇒ Manager) and would violate the range check. Exactly the bug caught in
    /// Phase 10 §1.3 (BUG-1), so the same <c>IsDefined</c> + digit guard is used here.
    /// </summary>
    public static WorkspaceRole ParseRole(string? role)
    {
        var value = role?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return WorkspaceRole.Member; // roadmap default
        }

        if (value.All(char.IsAsciiDigit))
        {
            throw new BadRequestException("Vai trò không hợp lệ. Chỉ nhận Admin, Manager hoặc Member.");
        }

        if (!Enum.TryParse<WorkspaceRole>(value, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new BadRequestException("Vai trò không hợp lệ. Chỉ nhận Admin, Manager hoặc Member.");
        }

        return parsed;
    }

    private static string GenerateRawToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(RawTokenBytes));

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private string BuildAcceptUrl(string rawToken)
        => $"{_settings.FrontendBaseUrl.TrimEnd('/')}/invitations/accept?token={Uri.EscapeDataString(rawToken)}";

    private Task<WorkspaceInvitation?> FindByTokenAsync(string token, CancellationToken ct)
    {
        var value = token?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return Task.FromResult<WorkspaceInvitation?>(null);
        }

        var hash = HashToken(value);

        return _db.WorkspaceInvitations.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
    }

    /// <summary>
    /// Lazy expiry (DB design §3.3 allows it): no background service is needed, because every read
    /// and every accept normalises the rows it is about to reason about.
    /// </summary>
    private async Task EnsureNotExpiredAsync(WorkspaceInvitation invitation, CancellationToken ct)
    {
        // Already terminal: 410 tells the accept page "this link is used up, ask for a new one",
        // which is more useful than a 404 for a row that plainly exists.
        if (invitation.Status is InvitationStatus.Expired or InvitationStatus.Cancelled)
        {
            throw new GoneException("Lời mời này đã hết hạn hoặc đã bị huỷ.");
        }

        if (invitation.Status != InvitationStatus.Pending
            || invitation.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return;
        }

        // Lazy expiry (DB design §3.3 allows it): no background service is needed, because every
        // read and every accept normalises the rows it is about to reason about.
        invitation.Status = InvitationStatus.Expired;
        await _db.SaveChangesAsync(ct);

        throw new GoneException("Lời mời này đã hết hạn.");
    }

    private async Task ExpireStaleAsync(Guid workspaceId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        await _db.WorkspaceInvitations
            .Where(i => i.WorkspaceId == workspaceId
                        && i.Status == InvitationStatus.Pending
                        && i.ExpiresAt < now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.Status, InvitationStatus.Expired), ct);
    }

    private static InvitationResponse ToResponse(
        WorkspaceInvitation invitation, string inviterName, bool emailSent)
        => new(
            invitation.Id,
            invitation.InvitedEmail,
            invitation.InvitedRole.ToString(),
            invitation.Status.ToString(),
            inviterName,
            invitation.ExpiresAt,
            invitation.CreatedAt,
            invitation.AcceptedByUserId,
            invitation.AcceptedAt,
            emailSent);
}
