using Microsoft.AspNetCore.Http;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Base for domain errors thrown by Board services. A group-level endpoint filter
/// translates them into JSON <c>{ error }</c> responses with the matching status code.
/// </summary>
public abstract class BoardModuleException : Exception
{
    protected BoardModuleException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

/// <summary>Resource does not exist or is not visible to the caller → 404.</summary>
public sealed class NotFoundException : BoardModuleException
{
    public NotFoundException(string message)
        : base(StatusCodes.Status404NotFound, message)
    {
    }
}

/// <summary>Caller is not authenticated (missing user id claim) → 401.</summary>
public sealed class UnauthorizedException : BoardModuleException
{
    public UnauthorizedException()
        : base(StatusCodes.Status401Unauthorized, "Authentication required.")
    {
    }
}

/// <summary>Caller is authenticated but lacks the required workspace role → 403.</summary>
public sealed class ForbiddenException : BoardModuleException
{
    public ForbiddenException(string message)
        : base(StatusCodes.Status403Forbidden, message)
    {
    }
}

/// <summary>Request conflicts with the current state (e.g. column still has tasks) → 409.</summary>
public sealed class ConflictException : BoardModuleException
{
    public ConflictException(string message)
        : base(StatusCodes.Status409Conflict, message)
    {
    }
}

/// <summary>Invalid input / failed validation → 400.</summary>
public sealed class BadRequestException : BoardModuleException
{
    public BadRequestException(string message)
        : base(StatusCodes.Status400BadRequest, message)
    {
    }
}
