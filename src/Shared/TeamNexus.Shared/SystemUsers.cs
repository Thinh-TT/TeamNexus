namespace TeamNexus.Shared;

/// <summary>
/// Well-known system account identifiers used by automated background/webhook flows (Phase 15.1 §T15.1.3).
/// System accounts have <c>users.is_system = true</c> and are blocked from interactive login.
/// </summary>
public static class SystemUsers
{
    public static readonly Guid GitHubBotUserId = Guid.Parse("00000000-0000-0000-0000-00000000b071");
    public const string GitHubBotUserName = "github-bot";
    public const string GitHubBotEmail = "github-bot@system.teamnexus.invalid";
    public const string GitHubBotDisplayName = "GitHub Bot";
}

