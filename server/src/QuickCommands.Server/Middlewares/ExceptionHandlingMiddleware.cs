using Microsoft.AspNetCore.Mvc;
using QuickCommands.Server.Exceptions;

namespace QuickCommands.Server.Middlewares;

/// <summary>
/// AppException → its status code and code; everything unexpected → 500, logged as error (which alerts via loggerui).
/// Expected failures are not logged here – the request log line covers them.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    IProblemDetailsService problemDetails,
    IHostEnvironment environment,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away – nothing to answer, nothing to alert.
            if (!context.Response.HasStarted)
                context.Response.StatusCode = 499;
        }
        catch (Exception e) when (!context.Response.HasStarted)
        {
            var (status, code, detail) = e switch
            {
                AppException app => (app.StatusCode, app.Code, app.Message),
                BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } =>
                    (StatusCodes.Status413PayloadTooLarge, "payload_too_large", "The request body is too large."),
                BadHttpRequestException bad => (bad.StatusCode, "bad_request", bad.Message),
                _ => (StatusCodes.Status500InternalServerError, "internal_error", "An unexpected error occurred."),
            };

            if (status >= 500)
            {
                logger.LogError(e, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path.Value);
                if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
                    detail = e.ToString();
            }

            context.Response.Clear();
            context.Response.StatusCode = status;
            var problem = new ProblemDetails { Status = status, Detail = detail };
            problem.Extensions["code"] = code;
            await problemDetails.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem, Exception = e });
        }
    }
}
