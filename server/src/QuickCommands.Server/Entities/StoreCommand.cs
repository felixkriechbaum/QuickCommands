namespace QuickCommands.Server.Entities;

/// <summary>Same values as the desktop app's ShellLanguage.</summary>
public enum ShellLanguage
{
    PowerShell,
    Cmd,
    Sh,
}

public sealed class StoreCommand
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PackageId { get; set; }
    public int SortOrder { get; set; }

    public string Name { get; set; } = "";
    public ShellLanguage Language { get; set; }
    public string Description { get; set; } = "";
    public string Script { get; set; } = "";

    /// <summary>The desktop parameter list as raw JSON. The server only stores it.</summary>
    public string? ParametersJson { get; set; }
}
