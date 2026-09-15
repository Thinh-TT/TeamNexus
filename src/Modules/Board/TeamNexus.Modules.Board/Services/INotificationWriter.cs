namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// One alert to fan out to a specific set of workspace members (Phase 11 §6.3).
/// <para>
/// Board declares this shape because Board is what detects the events (a task got an assignee, a
/// comment landed on someone's task); the Ai module owns the notification pipeline that turns it
/// into rows.
/// </para>
/// <param name="RecipientUserIds">
/// Explicit recipients, <b>not</b> a role filter: unlike the Observer's Manager/Admin fan-out, these
/// alerts are personal ("you were assigned", "someone commented on your task").
/// </param>
/// <param name="PayloadJson">Already-serialized camelCase jsonb, or null for a bare alert.</param>
public sealed record MemberNotification(
    Guid WorkspaceId,
    IReadOnlyCollection<Guid> RecipientUserIds,
    string Type,
    string Title,
    string Message,
    string? PayloadJson = null);

/// <summary>
/// Output port for user-facing (non-Observer) notifications (Phase 11 §6.3, decision D9).
/// <para>
/// The interface deliberately lives in the <b>Board</b> module: Board must not reference the Ai
/// module (Ai already references Board), so Board declares the port and module Ai supplies the EF
/// Core adapter (<c>NotificationWriter</c>). <c>AddBoardModule</c> registers a no-op so the Board
/// module stays self-sufficient; Ai overrides that registration — the same trick already used for
/// <see cref="IActivityLogWriter"/> and <see cref="IAiAgentResolver"/>.
/// </para>
/// <para>
/// <b>Implementations must never throw.</b> Notifying is a best-effort side effect of a write that
/// has already committed: a notification failure must not fail the task or comment that caused it.
/// </para>
/// </summary>
public interface INotificationWriter
{
    Task NotifyAsync(MemberNotification notification, CancellationToken ct = default);
}

/// <summary>
/// No-op writer used when the Ai module is not registered (module-scoped tests/harnesses) and as the
/// Board module's own default registration.
/// </summary>
public sealed class NullNotificationWriter : INotificationWriter
{
    public Task NotifyAsync(MemberNotification notification, CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>
/// <c>notifications.type</c> values produced for ordinary workspace members (Phase 11 §6.1).
/// <para>
/// Declared on the <b>Board</b> side, next to the port that writes them: Board is what detects these
/// events, so the vocabulary belongs with the events rather than with the module that persists them
/// (Board cannot reference Ai).
/// </para>
/// <para>
/// <b>Deliberately separate from the Observer's <c>NotificationTypes.All</c>.</b> That list is an
/// anti-hallucination whitelist — adding these there would let the Observer model emit
/// "TaskAssigned" and pass validation, inventing alerts about events it cannot see. These types are
/// written by our own code only, so they need no validation list (same reasoning as
/// <c>AgentNotificationTypes</c>, Phase 7).
/// </para>
/// </summary>
public static class MemberNotificationTypes
{
    /// <summary>A task was assigned to the recipient, or reassigned to them.</summary>
    public const string TaskAssigned = "TaskAssigned";

    /// <summary>Someone commented on a task the recipient is responsible for.</summary>
    public const string CommentOnTask = "CommentOnTask";

    /// <summary>
    /// The recipient was invited to a workspace. Reserved for an in-app mirror of the invitation:
    /// today the invitation travels by email, and this constant keeps the vocabulary stable so the
    /// frontend label map does not have to change when the mirror is added.
    /// </summary>
    public const string WorkspaceInvitation = "WorkspaceInvitation";

    /// <summary>
    /// Someone wrote <c>@tên</c> pointing at the recipient in a comment (Phase 12 §3).
    /// <para>
    /// Written by our own code from the ids the client sent explicitly — the server never parses
    /// <c>@tên</c> out of the text, so this type can never be produced by guessing somebody's name.
    /// </para>
    /// </summary>
    public const string CommentMention = "CommentMention";
}

/// <summary>
/// Column limits for the notification the Board module writes. Declared here (Board side) because
/// Board is what builds the title/message; the Ai module clamps again defensively, but a caller that
/// already respects the limit cannot turn a successful write into a database error.
/// </summary>
public static class MemberNotificationLimits
{
    /// <summary>Matches <c>notifications.title</c> varchar(200).</summary>
    public const int Title = 200;

    /// <summary>Matches <c>notifications.message</c> varchar(2000).</summary>
    public const int Message = 2000;

    /// <summary>A comment excerpt is a preview, never the full text (the alert is a pointer).</summary>
    public const int CommentExcerpt = 120;

    /// <summary>Upper bound on how many people one comment may mention.</summary>
    public const int MaxMentionedUsers = 20;

    /// <summary>Clamps a value to <paramref name="max"/> characters.</summary>
    public static string Clamp(string? value, int max)
    {
        var text = value ?? string.Empty;
        return text.Length <= max ? text : text[..max];
    }
}
