using QuickCommands.Server.Logging;
using QuickCommands.Server.Options;
using Serilog;
using Serilog.Configuration;
using Serilog.Debugging;
using Serilog.Events;

namespace QuickCommands.Server.Setup;

internal static class LoggingSetup
{
    /// <summary>
    /// Console for "docker logs", loggerui for alerts. Level discipline (see DESIGN.md): warn = reports only,
    /// error = unexpected failures only, framework logs reach the loggerui from Error on.
    /// </summary>
    public static void AddServerLogging(this WebApplicationBuilder builder)
    {
        var loggerUi = builder.Configuration.GetSection(LoggerUiOptions.Section).Get<LoggerUiOptions>() ?? new LoggerUiOptions();
        var environment = builder.Environment.EnvironmentName;

        // Problems of the loggerui sink itself (rejected batches, wrong token) end up in "docker logs".
        SelfLog.Enable(Console.Error);

        builder.Services.AddSerilog((_, lc) =>
        {
            lc.MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("env", environment)
                .Enrich.WithProperty("version", AppInfo.Version)
                .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture);

            if (loggerUi.IsEnabled)
                lc.WriteTo.Logger(sub => sub
                    .Filter.ByExcluding(IsFrameworkBelowError)
                    .WriteTo.Sink(new LoggerUiSink(loggerUi), new BatchingOptions
                    {
                        BatchSizeLimit = 200,
                        BufferingTimeLimit = TimeSpan.FromSeconds(2),
                        QueueLimit = 10_000,
                    }));
        });
    }

    private static bool IsFrameworkBelowError(LogEvent e) =>
        e.Level < LogEventLevel.Error &&
        e.Properties.TryGetValue("SourceContext", out var source) &&
        source is ScalarValue { Value: string name } &&
        (name.StartsWith("Microsoft.", StringComparison.Ordinal) || name.StartsWith("System.", StringComparison.Ordinal));
}
