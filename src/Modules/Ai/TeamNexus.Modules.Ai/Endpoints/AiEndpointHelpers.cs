using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TeamNexus.Modules.Board.Services;

namespace TeamNexus.Modules.Ai.Endpoints;

/// <summary>
/// Ai-module copy of the Board's <c>CurrentUser</c> helper (that one is <c>internal</c> to the Board
/// module, so it cannot be reused across modules). Reads the authenticated user id from the JWT
/// <see cref="ClaimTypes.NameIdentifier"/> claim; a missing/invalid claim throws
/// <see cref="UnauthorizedException"/> which <c>DomainExceptionFilter</c> maps to 401.
/// </summary>
internal static class AiEndpointHelpers
{
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
