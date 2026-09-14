using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// The "quick email" feature (Phase 11 §4.1): a Manager composes a short notice and the API sends it
/// to selected members over the same audited email gateway the invitations use.
/// <para>
/// Delivery is <b>partial by design</b>: one bad address must not discard the messages that did go
/// out, so the result reports <c>requested</c>/<c>sent</c>/<c>failed</c> plus the per-address errors
/// rather than failing the whole call.
/// </para>
/// </summary>
public interface IQuickEmailService
{
    Task<QuickEmailResult> SendAsync(
        Guid workspaceId, QuickEmailRequest request, Guid userId, CancellationToken ct = default);
}

public sealed class QuickEmailService : IQuickEmailService
{
    /// <summary>Matches the email template's subject column (email_messages.subject).</summary>
    public const int MaxSubjectLength = 200;

    /// <summary>Keeps one message well inside what a transactional provider will accept.</summary>
    public const int MaxBodyLength = 8000;

    internal const int MaxRecipientsPerQuickEmail = 50;

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IEmailGateway _email;
    private readonly WorkspaceEmailOptions _quota;
    private readonly ILogger<QuickEmailService> _logger;

    public QuickEmailService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IEmailGateway email,
        WorkspaceEmailOptions quota,
        ILogger<QuickEmailService> logger)
    {
        _db = db;
        _access = access;
        _email = email;
        _quota = quota;
        _logger = logger;
    }

    public async Task<QuickEmailResult> SendAsync(
        Guid workspaceId, QuickEmailRequest request, Guid userId, CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var subject = (request.Subject ?? string.Empty).Trim();
        if (subject.Length == 0 || subject.Length > MaxSubjectLength)
        {
            throw new BadRequestException($"Tiêu đề phải từ 1 đến {MaxSubjectLength} ký tự.");
        }

        var body = (request.Body ?? string.Empty).Trim();
        if (body.Length == 0 || body.Length > MaxBodyLength)
        {
            throw new BadRequestException($"Nội dung phải từ 1 đến {MaxBodyLength} ký tự.");
        }

        var recipientIds = (request.RecipientUserIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (recipientIds.Count == 0)
        {
            throw new BadRequestException("Cần chọn ít nhất một người nhận.");
        }

        var cap = Math.Clamp(
            _quota.MaxRecipientsPerQuickEmail <= 0
                ? MaxRecipientsPerQuickEmail
                : _quota.MaxRecipientsPerQuickEmail,
            1,
            MaxRecipientsPerQuickEmail);

        if (recipientIds.Count > cap)
        {
            throw new BadRequestException($"Chỉ được gửi tối đa {cap} người nhận mỗi lần.");
        }

        await EnforceHourlyQuotaAsync(workspaceId, recipientIds.Count, ct);

        // Recipients must be HUMANS in THIS workspace: the AI Agent has no inbox, and a user id from
        // another workspace would be an email sent on the strength of a workspace the caller does not
        // manage. One query, validated before anything is sent.
        var members = await _db.WorkspaceMembers
            .AsNoTracking()
            .Where(wm => wm.WorkspaceId == workspaceId
                         && recipientIds.Contains(wm.UserId)
                         && wm.MemberType == MemberType.Human)
            .Select(wm => new { wm.UserId, wm.User!.DisplayName, wm.User.Email })
            .ToListAsync(ct);

        var unknown = recipientIds.Except(members.Select(m => m.UserId)).ToList();
        if (unknown.Count > 0)
        {
            throw new BadRequestException(
                "Có người nhận không phải thành viên của workspace này (hoặc là AI Agent).");
        }

        var workspaceName = await _db.Workspaces
            .Where(w => w.Id == workspaceId)
            .Select(w => w.Name)
            .FirstOrDefaultAsync(ct) ?? "workspace";

        var senderName = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct) ?? "Quản lý";

        var requested = 0;
        var sent = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var member in members)
        {
            // Sending a notice to yourself is never what the Manager meant; skip silently rather
            // than reporting a failure for a message that was correctly not sent.
            if (member.UserId == userId)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(member.Email))
            {
                requested++;
                failed++;
                errors.Add($"{member.DisplayName}: không có địa chỉ email.");
                continue;
            }

            requested++;

            var ok = await _email.SendQuickEmailAsync(
                workspaceId,
                userId,
                member.Email!,
                workspaceName,
                senderName,
                subject,
                body,
                ct);

            if (ok)
            {
                sent++;
            }
            else
            {
                failed++;
                errors.Add($"{member.DisplayName}: gửi thất bại.");
            }
        }

        _logger.LogInformation(
            "Quick email tới workspace {WorkspaceId}: requested={Requested}, sent={Sent}, failed={Failed}.",
            workspaceId,
            requested,
            sent,
            failed);

        return new QuickEmailResult(requested, sent, failed, errors);
    }

    /// <summary>
    /// Counts every email logged for this workspace in the last hour — invitations included, because
    /// the quota is a property of the account, not of one feature.
    /// </summary>
    private async Task EnforceHourlyQuotaAsync(Guid workspaceId, int pending, CancellationToken ct)
    {
        var limit = Math.Max(1, _quota.MaxEmailsPerHourPerWorkspace);
        var windowStart = DateTimeOffset.UtcNow.AddHours(-1);

        var used = await _db.EmailMessages
            .AsNoTracking()
            .CountAsync(m => m.WorkspaceId == workspaceId && m.CreatedAt >= windowStart, ct);

        if (used + pending <= limit)
        {
            return;
        }

        var oldest = await _db.EmailMessages
            .AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.CreatedAt >= windowStart)
            .OrderBy(m => m.CreatedAt)
            .Select(m => (DateTimeOffset?)m.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var minutes = oldest is null
            ? 60
            : (int)Math.Ceiling(Math.Max(1, (oldest.Value.AddHours(1) - DateTimeOffset.UtcNow).TotalMinutes));

        throw new TooManyRequestsException(
            $"Workspace đã gửi {used}/{limit} email trong giờ qua. Vui lòng thử lại sau khoảng {minutes} phút.");
    }
}
