using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuickCommands.Server.Data;
using QuickCommands.Server.Middlewares;
using QuickCommands.Server.Options;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

namespace QuickCommands.Server.Setup;

internal static class Pipeline
{
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        if (AppInfo.IsGeneratingOpenApiDocument || !app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.MigrateOnStartup)
            return;
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public static void UseServerPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseMiddleware<RequestIdMiddleware>();
        app.UseSerilogRequestLogging(o =>
        {
            o.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0} ms";
            // Unexpected failures are logged by the ExceptionHandlingMiddleware (with stack trace) – one error per failure.
            o.GetLevel = (_, _, _) => LogEventLevel.Information;
        });
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseStatusCodePages();
        app.UseRouting();
        app.UseAuthentication();
        app.UseMiddleware<CurrentUserMiddleware>();
        app.UseRateLimiter();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }
        app.MapControllers();
    }
}
