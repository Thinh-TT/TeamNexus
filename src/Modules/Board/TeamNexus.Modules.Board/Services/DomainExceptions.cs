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

/// <summary>
/// The resource existed but is no longer usable — an expired or cancelled invitation token → 410.
/// Distinct from 404 on purpose: the accept page can tell "link expired, ask for a new one" apart
/// from "link does not exist".
/// </summary>
public sealed class GoneException : BoardModuleException
{
    public GoneException(string message)
        : base(StatusCodes.Status410Gone, message)
    {
    }
}

/// <summary>
/// A metered quota is exhausted — the free-tier email allowance → 429.
/// Distinct from 409 on purpose: the request is valid and retrying the same thing later will work.
/// </summary>
public sealed class TooManyRequestsException : BoardModuleException
{
    public TooManyRequestsException(string message)
        : base(StatusCodes.Status429TooManyRequests, message)
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
