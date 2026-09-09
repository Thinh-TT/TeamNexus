using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace TeamNexus.Modules.Auth.Endpoints;

/// <summary>
/// Endpoint filter enforcing the anti-CSRF token on state-changing endpoints:
/// the client must echo the antiforgery request token (from the XSRF-TOKEN cookie)
/// in the X-XSRF-TOKEN header (Phase 1 §3.2).
/// </summary>
public sealed class AntiforgeryValidationEndpointFilter : IEndpointFilter
{
    private readonly IAntiforgery _antiforgery;

    public AntiforgeryValidationEndpointFilter(IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!await _antiforgery.IsRequestValidAsync(context.HttpContext))
        {
            return Results.Json(
                new { error = "CSRF token missing or invalid (send X-XSRF-TOKEN header)." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}
