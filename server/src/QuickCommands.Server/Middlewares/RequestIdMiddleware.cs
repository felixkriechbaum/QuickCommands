using System.Diagnostics;
using System.Text.RegularExpressions;
using Serilog.Context;

namespace QuickCommands.Server.Middlewares;

/// <summary>Takes X-Request-Id from the client (or creates one), returns it and adds requestId/traceId to every log event.</summary>
public sealed partial class RequestIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Request-Id";

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex ValidId();

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var requestId = ValidId().IsMatch(incoming) ? incoming : Guid.NewGuid().ToString("N");
        context.TraceIdentifier = requestId;
        context.Response.Headers[HeaderName] = requestId;

        using (LogContext.PushProperty("requestId", requestId))
        using (LogContext.PushProperty("traceId", Activity.Current?.TraceId.ToString() ?? ""))
            await next(context);
    }
}
