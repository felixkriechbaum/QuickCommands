using QuickCommands.Server.Setup;

if (args.Contains(HealthProbe.Argument))
    return await HealthProbe.RunAsync();

var builder = WebApplication.CreateBuilder(args);
builder.AddServerLogging();
builder.Services.AddServer(builder.Configuration);

var app = builder.Build();
await app.MigrateDatabaseAsync();
app.UseServerPipeline();
await app.RunAsync();
return 0;

/// <summary>Entry point; public so the integration tests can use WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
