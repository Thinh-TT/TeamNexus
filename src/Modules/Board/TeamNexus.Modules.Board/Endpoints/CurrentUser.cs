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
}
