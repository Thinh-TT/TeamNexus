using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Ai.Services.Email;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Outcome of one digest pass, for logging and for the tests that assert idempotency.
/// </summary>
/// <param name="Skipped">True when the feature is off, or when it is not yet due for today.</param>
/// <param name="Considered">Recipients examined this pass.</param>
/// <param name="SkippedAlreadySent">Recipients that already have a digest row for today.</param>
/// <param name="SkippedEmpty">Recipients with nothing to report (no e-mail is sent for these).</param>
/// <param name="Sent">Digests the transport accepted.</param>
/// <param name="Failed">Digests the transport rejected (a <c>Failed</c> audit row exists for each).</param>
public sealed record DigestRunResult(
    bool Skipped,
    int Considered,
    int SkippedAlreadySent,
    int SkippedEmpty,
    int Sent,
    int Failed)
{
    /// <summary>Nothing to do — the feature is off, or today's send time has not arrived yet.</summary>
    public static DigestRunResult NotRun { get; } = new(true, 0, 0, 0, 0, 0);

    /// <summary>Total <c>email_messages</c> rows a run with this result should have created.</summary>
    public int Attempted => Sent + Failed;
}

/// <summary>
/// One digest pass (Phase 13 §3.3).
/// <para>
/// Extracted from the hosted service so it can be exercised <b>directly</b> by an integration test with no
/// timer, no startup delay and no waiting — the same reason <c>ObserverBackgroundService</c> and
/// <c>ObserverService</c> are separate types. The service decides <i>when</i>; this decides <i>what</i>.
/// </para>
/// </summary>
public interface IDailyDigestRunner
{
    Task<DigestRunResult> RunOnceAsync(CancellationToken ct = default);
}

/// <summary>
/// Sends at most one daily digest per recipient, deduplicated by the audit table itself.
/// <para>
/// <b>Why the dedupe lives in the database and not in a field here:</b> on a free-tier host the process
/// sleeps and restarts several times a day, so any in-memory "already sent" flag is reset by every cold
/// start and the same person gets several digests. A row in <c>email_messages</c> survives all of that, so
/// <c>(kind = DailyDigest, to_email, created today)</c> is the idempotency key. That is also why this class
/// needs no extra column on <c>users</c>.
/// </para>
/// <para>
/// <b>A failed row counts as "handled".</b> If the provider rejected today's send, retrying in the next
/// poll would keep hammering an outage and burn the monthly quota for one person. Missing one day's digest
/// is the cheaper failure.
/// </para>
/// </summary>
public sealed class DailyDigestRunner : IDailyDigestRunner
{
    private readonly TeamNexusDbContext _db;
    private readonly IDailyDigestService _content;
    private readonly IDigestGateway _gateway;
    private readonly DigestOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<DailyDigestRunner> _logger;

    public DailyDigestRunner(
        TeamNexusDbContext db,
        IDailyDigestService content,
        IDigestGateway gateway,
        IOptions<DigestOptions> options,
        TimeProvider clock,
        ILogger<DailyDigestRunner> logger)
    {
        _db = db;
        _content = content;
        _gateway = gateway;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<DigestRunResult> RunOnceAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return DigestRunResult.NotRun;
        }

        var offset = _options.EffectiveTimeZoneOffsetMinutes;
        var now = _clock.GetUtcNow();
        var todayLocal = DateOnly.FromDateTime(now.AddMinutes(offset).UtcDateTime);

        // Not yet the send hour where the readers are? Then today's digest is simply not due — the next
        // poll will pick it up. This is also what makes a 1-hour poll safe.
        if (now < _options.DueAt(offset, todayLocal))
        {
            return DigestRunResult.NotRun;
        }

        // Midnight UTC of the recipient's day, used as the "already handled today" boundary. Comparing
        // against rows rather than a stored timestamp means a host restart cannot lose the state.
        var dayStartUtc = new DateTimeOffset(todayLocal.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            .AddMinutes(-offset);

        var alreadySent = await _db.EmailMessages
            .AsNoTracking()
            .Where(m => m.Kind == EmailKinds.DailyDigest && m.CreatedAt >= dayStartUtc)
            .Select(m => m.ToEmail)
            .Distinct()
            .ToListAsync(ct);

        var handled = new HashSet<string>(alreadySent, StringComparer.OrdinalIgnoreCase);

        // Ordered by id so a capped run is deterministic (the same people are not starved run after run).
        var recipients = await _db.Users
            .AsNoTracking()
            .Where(u => u.DigestEnabled && u.Email != null)
            .OrderBy(u => u.Id)
            .Select(u => new { u.Id, u.Email })
            .Take(_options.EffectiveMaxUsersPerRun)
            .ToListAsync(ct);

        var considered = 0;
        var skippedAlreadySent = 0;
        var skippedEmpty = 0;
        var sent = 0;
        var failed = 0;

        foreach (var recipient in recipients)
        {
            ct.ThrowIfCancellationRequested();

            if (sent + failed >= _options.EffectiveMaxDailyEmails)
            {
                _logger.LogWarning(
                    "Digest: đã đạt hạn mức {Max} email trong một lượt — phần còn lại sẽ gửi ở lượt sau.",
                    _options.EffectiveMaxDailyEmails);
                break;
            }

            if (recipient.Email is null || handled.Contains(recipient.Email))
            {
                skippedAlreadySent++;
                continue;
            }

            considered++;

            DailyDigestContent? content;

            try
            {
                content = await _content.BuildAsync(
                    recipient.Id,
                    todayLocal,
                    _options.EffectiveMaxWorkspacesPerUser,
                    _options.EffectiveMaxItemsPerBucket,
                    ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One unreadable recipient must not abort the run for everybody else.
                _logger.LogError(ex, "Không dựng được nội dung digest cho {UserId}.", recipient.Id);
                failed++;
                continue;
            }

            if (content is null)
            {
                skippedEmpty++;
                continue;
            }

            if (await _gateway.SendDailyDigestAsync(content, ct))
            {
                sent++;
            }
            else
            {
                failed++;
            }

            // Marked handled either way: see the class remarks about retrying into an outage.
            handled.Add(recipient.Email);
        }

        // One line, and deliberately not one line per recipient: the C# logger takes no redaction, so the
        // run is summarised and the addresses are masked instead of being written in full.
        _logger.LogInformation(
            "Digest: considered={Considered}, alreadySent={AlreadySent}, empty={Empty}, sent={Sent}, failed={Failed}, "
            + "day={Day}, tzOffset={Offset}, sample={Sample}.",
            considered,
            skippedAlreadySent,
            skippedEmpty,
            sent,
            failed,
            todayLocal,
            offset,
            Mask(recipients.FirstOrDefault(r => r.Email is not null)?.Email));

        return new DigestRunResult(false, considered, skippedAlreadySent, skippedEmpty, sent, failed);
    }

    /// <summary>
    /// Masks an address for the log (<c>an@example.test</c> ⇒ <c>a***@example.test</c>).
    /// <para>
    /// The log is the one place a recipient's address could leak into an ordinary ops file, and a digest
    /// run only needs to prove <i>that</i> it worked, never <i>for whom</i>.
    /// </para>
    /// </summary>
    private static string Mask(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "none";
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);

        return at <= 0 ? "***" : $"{email[0]}***{email[at..]}";
    }
}
