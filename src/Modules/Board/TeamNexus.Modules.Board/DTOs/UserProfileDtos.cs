namespace TeamNexus.Modules.Board.DTOs;

/// <summary>
/// The signed-in user's own profile (Phase 11 §5.2).
/// </summary>
public sealed record UserProfileResponse(
    Guid Id, string Email, string DisplayName, string? AvatarUrl, DateTimeOffset CreatedAt);

/// <summary>
/// Editable profile fields. <c>AvatarUrl</c> empty ⇒ remove the avatar; a non-empty value must be an
/// absolute http/https URL (rejected with 400 otherwise, never silently dropped).
/// </summary>
public sealed record UpdateProfileRequest(string DisplayName, string? AvatarUrl);

/// <summary>
/// One workspace the caller belongs to, as shown on the profile page.
/// <para>
/// Deliberately <b>not</b> <c>WorkspaceSummaryResponse</c>: that payload comes from
/// <c>WorkspaceService.ListForUserAsync</c>, which lazily creates a default workspace — a side effect
/// the dashboard depends on but a read-only profile page must not trigger.
/// </para>
/// </summary>
public sealed record MyWorkspaceResponse(
    Guid Id, string Name, string? Description, string Role, Guid OwnerId, bool IsOwner);
