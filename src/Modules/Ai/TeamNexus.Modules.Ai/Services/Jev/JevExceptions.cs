using Microsoft.AspNetCore.Http;
using TeamNexus.Modules.Board.Services;

namespace TeamNexus.Modules.Ai.Services.Jev;

/// <summary>
/// A Jev decision call failed: HTTP ≠ 2xx, timeout, transport error, or an unparsable body →
/// <b>502 Bad Gateway</b>, exactly like <see cref="AiProviderException"/>.
/// <para>
/// Inherits <see cref="BoardModuleException"/> so the existing <c>DomainExceptionFilter</c> maps it to
/// <c>{ error }</c> + status without a filter of its own — the same reason
/// <see cref="AiProviderException"/> is shaped this way.
/// </para>
/// <para>
/// The API key is never part of the message or of any log line written by the provider, so a leaked
/// exception can never leak the secret.
/// </para>
/// </summary>
public sealed class JevDecisionException : BoardModuleException
{
    public JevDecisionException(string message)
        : base(StatusCodes.Status502BadGateway, message)
    {
    }
}

/// <summary>
/// Jev is switched off (<c>Jev:Enabled = false</c>) → <b>503 Service Unavailable</b>, the
/// <c>AgentDisabledException</c>/<c>Reports:Enabled</c> precedent.
/// <para>
/// Thrown <b>before</b> any query, so a disabled feature reveals neither which board/task exists nor
/// spends a token.
/// </para>
/// <para>
/// Note the deliberate asymmetry with <see cref="AiChatDisabledException"/>: an <b>empty</b>
/// <c>Jev:ApiKey</c> is <b>not</b> an error — it selects <c>FakeJevDecisionProvider</c>, so a fresh clone,
/// a dev machine and the CI test run all exercise the full flow offline at zero cost.
/// </para>
/// </summary>
public sealed class JevDisabledException : BoardModuleException
{
    public JevDisabledException()
        : base(StatusCodes.Status503ServiceUnavailable, "Jev decision service is disabled.")
    {
    }
}
