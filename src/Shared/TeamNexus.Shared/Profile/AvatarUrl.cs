namespace TeamNexus.Shared.Profile;

/// <summary>
/// Validation for the avatar URL a user may set on their own profile (Phase 11 §5.1).
/// <para>
/// Pure functions with no dependency, so both the API and its unit tests agree on one rule. This is
/// the backend counterpart of the frontend's <c>isSafeHref</c>
/// (<c>frontend/src/features/board/utils/markdown.tsx</c>): the two must stay in sync, because the UI
/// pre-validates and the server is what actually decides.
/// </para>
/// <para>
/// <b>Only absolute <c>http:</c>/<c>https:</c> URLs are accepted.</b> The value ends up in an
/// <c>&lt;img src&gt;</c> on every page that renders the user, so <c>javascript:</c>,
/// <c>data:</c>, <c>file:</c> and <c>vbscript:</c> are rejected outright, and a scheme-relative
/// <c>//host/x</c> is rejected too — it silently inherits the page's scheme and is a classic way to
/// smuggle an unexpected origin past a "starts with http" check.
/// </para>
/// </summary>
public static class AvatarUrl
{
    /// <summary>Long enough for any CDN/Gravatar URL, short enough for a text column.</summary>
    public const int MaxLength = 2048;

    private static readonly string[] AllowedSchemes = ["http:", "https:"];

    /// <summary>
    /// True when <paramref name="url"/> is an absolute http/https URL within the length limit.
    /// </summary>
    public static bool IsSafe(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true; // "no avatar" is always valid
        }

        var value = url.Trim();

        if (value.Length > MaxLength)
        {
            return false;
        }

        if (!AllowedSchemes.Any(scheme => value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var parsed)
               && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
               && !string.IsNullOrEmpty(parsed.Host);
    }

    /// <summary>
    /// Trims the value and maps an empty/whitespace string to <c>null</c> ("remove my avatar").
    /// Does <b>not</b> validate — call <see cref="IsSafe"/> first, so an unusable URL is reported as a
    /// 400 instead of being silently discarded.
    /// </summary>
    public static string? Normalize(string? url)
        => string.IsNullOrWhiteSpace(url) ? null : url.Trim();
}
