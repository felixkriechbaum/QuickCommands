namespace QuickCommands.Server.Exceptions;

/// <summary>An expected failure. The ExceptionHandlingMiddleware turns it into ProblemDetails with <see cref="Code"/>.</summary>
public abstract class AppException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    /// <summary>Machine-readable error code (snake_case), stable across versions.</summary>
    public string Code { get; } = code;
}

public sealed class BadRequestException(string code, string message) : AppException(StatusCodes.Status400BadRequest, code, message);

public sealed class ForbiddenException(string code = "forbidden", string message = "You are not allowed to do this.")
    : AppException(StatusCodes.Status403Forbidden, code, message);

public sealed class NotFoundException(string code = "not_found", string message = "The resource does not exist.")
    : AppException(StatusCodes.Status404NotFound, code, message);

public sealed class ConflictException(string code, string message) : AppException(StatusCodes.Status409Conflict, code, message);

public sealed class PreconditionFailedException(string code, string message)
    : AppException(StatusCodes.Status412PreconditionFailed, code, message);

public sealed class PreconditionRequiredException(string message)
    : AppException(StatusCodes.Status428PreconditionRequired, "precondition_required", message);

public sealed class PayloadTooLargeException(string message)
    : AppException(StatusCodes.Status413PayloadTooLarge, "payload_too_large", message);
