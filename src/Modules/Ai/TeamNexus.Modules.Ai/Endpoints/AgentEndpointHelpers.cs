namespace TeamNexus.Modules.Ai.Endpoints;

/// <summary>
/// Agent-route constants (Phase 7 §4.9). The <c>RequireUserId</c> helper the agent endpoints use lives
/// in <see cref="AiEndpointHelpers"/> — the Ai module already had its own copy of the Board's
/// <c>internal</c> <c>CurrentUser</c> helper (Phase 4 §3), and a second extension method with the same
/// name in this namespace would make every call site ambiguous.
/// </summary>
internal static class AgentEndpointHelpers
{
    /// <summary>Default page size of the run history (route 3).</summary>
    public const int DefaultRunsTake = 20;

    /// <summary>Upper bound of the run history page size (route 3).</summary>
    public const int MaxRunsTake = 50;
}
