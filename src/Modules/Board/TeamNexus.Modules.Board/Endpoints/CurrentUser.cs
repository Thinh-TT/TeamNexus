using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TeamNexus.Modules.Board.Services;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>Reads the authenticated user's id from the JWT NameIdentifier claim.</summary>
internal static class CurrentUser
{
    /// <summary>Returns the user id or throws <see cref="UnauthorizedException"/> (handled → 401).</summary>
    public static Guid RequireUserId(this HttpContext http)
    {
        var value = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(value, out var id))
        {
            return id;
        }

        throw new UnauthorizedException();
    }

    /// <summary>
    /// The authenticated user's email claim, or null when it is absent.
    /// <para>
    /// Phase 11 uses this only to prove that the caller is the person an invitation was addressed to.
    /// A missing claim is not an error here: the invitation service turns "no email" into the same
    /// 403 as "different email", so an unusual token shape cannot silently accept an invitation.
    /// </para>
    /// </summary>
    public static string? GetUserEmail(this HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.Email)
           ?? http.User.FindFirstValue("email");
}
