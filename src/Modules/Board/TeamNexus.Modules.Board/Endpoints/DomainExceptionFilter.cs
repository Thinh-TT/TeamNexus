using Microsoft.AspNetCore.Http;
using TeamNexus.Modules.Board.Services;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>
/// Translates <see cref="BoardModuleException"/> (thrown by services) into JSON
/// <c>{ error }</c> responses with the exception's status code. Applied once per
/// Board route group.
/// </summary>
public sealed class DomainExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (BoardModuleException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
        }
    }
}
