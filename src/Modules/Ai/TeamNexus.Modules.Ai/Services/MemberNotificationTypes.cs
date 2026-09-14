namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// <c>notifications.type</c> values announced for ordinary workspace members.
/// <para>
/// The canonical constants live on the <b>Board</b> side
/// (<c>TeamNexus.Modules.Board.Services.MemberNotificationTypes</c>), next to the port that writes
/// them and to the services that detect the events. This type alias exists so the Ai module — which
/// owns the notification pipeline and its tests — can refer to the same vocabulary without reaching
/// into a Board service type at every call site.
/// </para>
/// <para>
/// <b>Deliberately a separate vocabulary from <see cref="NotificationTypes"/>.</b>
/// <c>NotificationTypes.All</c> is the Observer's anti-hallucination whitelist
/// (<c>ObserverFindingValidator</c>): adding these would let the Observer model emit "TaskAssigned"
/// and pass validation, inventing alerts about events it cannot see. These types are written by our
/// own code only, so they need no validation list — exactly the reasoning behind
/// <c>AgentNotificationTypes</c> (Phase 7).
/// </para>
/// </summary>
public static class MemberNotificationTypes
{
    /// <summary>A task was assigned to the recipient, or reassigned to them.</summary>
    public const string TaskAssigned = Board.Services.MemberNotificationTypes.TaskAssigned;

    /// <summary>Someone commented on a task the recipient is responsible for.</summary>
    public const string CommentOnTask = Board.Services.MemberNotificationTypes.CommentOnTask;

    /// <summary>The recipient was invited to a workspace (reserved for the in-app mirror).</summary>
    public const string WorkspaceInvitation = Board.Services.MemberNotificationTypes.WorkspaceInvitation;
}
