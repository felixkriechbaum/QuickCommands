namespace QuickCommands.Server.Options;

public sealed class DatabaseOptions
{
    public const string Section = "Database";
    public const string ConnectionStringName = "DefaultConnection";

    /// <summary>MySQL version the SQL is generated for. Fixed instead of auto-detected so startup and migrations never need a live connection.</summary>
    public string ServerVersion { get; set; } = "8.4.0-mysql";

    public bool MigrateOnStartup { get; set; } = true;
}
