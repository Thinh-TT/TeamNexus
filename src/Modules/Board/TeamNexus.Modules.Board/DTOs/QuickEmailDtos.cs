namespace TeamNexus.Modules.Board.DTOs;

/// <summary>
/// One "quick email" composed by a Manager (Phase 11 §4.1): a short notice sent to members of the
/// workspace over the audited email gateway (meeting reminders, urgent items).
/// </summary>
/// <param name="RecipientUserIds">Members of this workspace; the AI Agent is rejected (no inbox).</param>
public sealed record QuickEmailRequest(
    string Subject, string Body, IReadOnlyList<Guid> RecipientUserIds);

/// <summary>
/// Outcome of a quick-email fan-out.
/// <para>
/// <see cref="Requested"/> counts the messages actually attempted (the sender themselves is never
/// one), so a Manager who selects four people including themselves sees three. A partial failure is
/// still a <b>200</b>: the messages that went out are real and the caller needs the per-address
/// reasons, not a single all-or-nothing error.
/// </para>
/// </summary>
public sealed record QuickEmailResult(
    int Requested, int Sent, int Failed, IReadOnlyList<string> Errors);
