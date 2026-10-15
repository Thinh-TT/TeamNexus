namespace TeamNexus.Modules.Ai.Services.Jev;

/// <summary>
/// The team of specialised AI Agents (Giai đoạn 14.7 Track B §B1) — the vocabulary that turns the single
/// Phase 7 executor into a small staff of virtual co-workers.
/// <para>
/// <b>Why a pure static catalogue rather than a table.</b> A persona is a <i>prompt and a skill set</i>, not
/// workspace data: nothing here is configurable per workspace, and a database row would only add a seeding
/// step, a migration and a failure mode. The closed set is also what makes the "one agent row per persona"
/// index meaningful — an open vocabulary would make the unique constraint arbitrary.
/// </para>
/// <para>
/// <b><see cref="GeneralKey"/> must keep its Phase 7 identity.</b> Before this phase a workspace had exactly
/// one agent, created as the user <c>ai-agent-{workspaceId:N}</c>. That row is the persona <c>general</c>
/// (the migration backfills it), and its user name must NOT acquire a persona segment: the verification
/// harness, <c>FindAgentUserIdAsync</c> and every Phase 7 test already look for that exact handle, so
/// renaming it would be a silent data-identity change rather than a feature.
/// </para>
/// <para>
/// The whitelist lives here (Ai side) and Board only ever sees the resulting row: Board must not reference
/// Ai, so it learns a persona through <c>IAiAgentResolver.EnsurePersonaAgentAsync</c> instead of through this
/// type. The database CHECK constraint lists the same four keys — the two must be changed together.
/// </para>
/// </summary>
public static class PersonaCatalog
{
    /// <summary>The pre-existing agent. Its skill set is EMPTY on purpose (backward-compat with Phase 7).</summary>
    public const string GeneralKey = "general";

    /// <summary>Plans and shapes the work: user stories, acceptance criteria, sprint structure.</summary>
    public const string ScrumMasterKey = "scrum_master";

    /// <summary>Reviews design and implementation: architecture, code review, API contracts.</summary>
    public const string TechLeadKey = "tech_lead";

    /// <summary>Attacks the work: test design, security audit, threat modelling.</summary>
    public const string QaSecurityKey = "qa_security";

    /// <summary>
    /// Every persona, in presentation order. <see cref="GeneralKey"/> is first and is also the default, so a
    /// caller that does not care about personas lands exactly where Phase 7 left off.
    /// </summary>
    public static IReadOnlyList<AgentPersona> All { get; } =
    [
        // Skills empty: this persona IS the Phase 7 agent, and giving it skills now would change the
        // dispatching input of an already-shipped feature without anyone asking for it.
        new(GeneralKey, "TeamNexus Agent", []),

        new(ScrumMasterKey, "AI Scrum Master",
            ["user-story", "acceptance-criteria", "sprint-planning"]),

        new(TechLeadKey, "AI Tech Lead",
            ["architecture", "code-review", "api-design"]),

        new(QaSecurityKey, "AI QA & Security",
            ["test-cases", "security-audit", "threat-modeling"]),
    ];

    /// <summary>The persona used when a caller does not name one.</summary>
    public static AgentPersona Default => All[0];

    /// <summary>True when <paramref name="personaKey"/> is inside the whitelist (case-insensitive).</summary>
    public static bool IsKnown(string? personaKey) => Canonical(personaKey) is not null;

    /// <summary>
    /// Canonical (lowercase) persona key, or <c>null</c> when it is not in the whitelist.
    /// <para>
    /// Returns <c>null</c> rather than falling back to <c>general</c>: silently treating a typo as "the
    /// default persona" hides a real client bug behind plausible-looking output, which is exactly the class of
    /// failure this phase is supposed to eliminate.
    /// </para>
    /// </summary>
    public static string? Canonical(string? personaKey)
    {
        if (string.IsNullOrWhiteSpace(personaKey))
        {
            return null;
        }

        var trimmed = personaKey.Trim();

        return All
            .Select(persona => persona.Key)
            .FirstOrDefault(known => string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The persona for a key, or <c>null</c> when the key is unknown.</summary>
    public static AgentPersona? Find(string? personaKey)
    {
        var canonical = Canonical(personaKey);

        return canonical is null
            ? null
            : All.First(persona => string.Equals(persona.Key, canonical, StringComparison.Ordinal));
    }

    /// <summary>The display name for a persona, falling back to the general one for an unknown key.</summary>
    public static string DisplayNameFor(string personaKey)
        => Find(personaKey)?.DisplayName ?? Default.DisplayName;

    /// <summary>The default skill slugs for a persona; empty for an unknown key.</summary>
    public static IReadOnlyList<string> SkillsFor(string personaKey)
        => Find(personaKey)?.Skills ?? [];

    /// <summary>
    /// The <c>users.user_name</c> handle of a persona's agent row.
    /// <para>
    /// <b><see cref="GeneralKey"/> deliberately has no persona segment</b> — see the class remarks: it is the
    /// handle Phase 7 created, and it must stay byte-identical. Every other persona gets
    /// <c>ai-agent-{persona}-{workspaceId:N}</c>, which is unique per (workspace, persona), readable in the
    /// database, and the same shape the harness uses to trace/clean up agent rows.
    /// </para>
    /// </summary>
    public static string AgentUserName(Guid workspaceId, string personaKey)
    {
        var canonical = Canonical(personaKey) ?? GeneralKey;

        return string.Equals(canonical, GeneralKey, StringComparison.Ordinal)
            ? $"ai-agent-{workspaceId:N}"
            : $"ai-agent-{canonical}-{workspaceId:N}";
    }
}

/// <summary>
/// One persona: its stable <paramref name="Key"/> (the value stored in
/// <c>workspace_members.persona_key</c>), the <paramref name="DisplayName"/> a human sees, and the
/// <paramref name="Skills"/> an agent row starts with so assignee dispatching has something to match on.
/// </summary>
public sealed record AgentPersona(string Key, string DisplayName, IReadOnlyList<string> Skills);
