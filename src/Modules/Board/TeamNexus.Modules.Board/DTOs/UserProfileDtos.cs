namespace TeamNexus.Modules.Board.DTOs;

/// <summary>
/// The signed-in user's own profile (Phase 11 §5.2, extended in Phase 13 §3.4).
/// </summary>
/// <param name="DigestEnabled">
/// Whether the daily work digest may be emailed to this user (Phase 13 §3). <b>Appended as the last
/// field</b>: the payload is additive, so a client written before Phase 13 keeps working unchanged.
/// </param>
public sealed record UserProfileResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    DateTimeOffset CreatedAt,
    bool DigestEnabled);

/// <summary>
/// Editable profile fields. <c>AvatarUrl</c> empty ⇒ remove the avatar; a non-empty value must be an
/// absolute http/https URL (rejected with 400 otherwise, never silently dropped).
/// </summary>
/// <param name="DigestEnabled">
/// Daily-digest opt-in/out (Phase 13 §3.4). <b><c>null</c> ⇒ leave the stored value alone</b>, which is
/// what makes the new field non-breaking: a pre-Phase-13 client that sends only
/// <c>{ displayName, avatarUrl }</c> must not silently switch the digest off. <c>false</c> is a real
/// instruction and is therefore distinct from "not mentioned".
/// </param>
public sealed record UpdateProfileRequest(
    string DisplayName,
    string? AvatarUrl,
    bool? DigestEnabled = null);

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
