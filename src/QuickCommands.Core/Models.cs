namespace QuickCommands.Core;

/// <summary>The script language a command is written in. Each command belongs to exactly one.</summary>
public enum ShellLanguage
{
    PowerShell,
    Cmd,
    Sh,
}

/// <summary>A concrete shell the commands can be installed into.</summary>
public enum ShellTarget
{
    /// <summary>PowerShell 7+ (pwsh).</summary>
    Pwsh,

    /// <summary>Windows PowerShell 5.1.</summary>
    WindowsPowerShell,

    Cmd,
    Bash,
    Zsh,
}

public static class ShellTargetExtensions
{
    public static ShellLanguage Language(this ShellTarget target) => target switch
    {
        ShellTarget.Pwsh or ShellTarget.WindowsPowerShell => ShellLanguage.PowerShell,
        ShellTarget.Cmd => ShellLanguage.Cmd,
        _ => ShellLanguage.Sh,
    };

    /// <summary>cmd and Windows PowerShell only exist on Windows; zsh is not supported on Windows.</summary>
    public static bool IsSupportedOnCurrentOs(this ShellTarget target) => target switch
    {
        ShellTarget.Cmd or ShellTarget.WindowsPowerShell => OperatingSystem.IsWindows(),
        ShellTarget.Zsh => !OperatingSystem.IsWindows(),
        _ => true,
    };

    public static bool UsesProfileFile(this ShellTarget target) => target != ShellTarget.Cmd;
}

public sealed class QuickCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public ShellLanguage Language { get; set; }
    public string Description { get; set; } = "";
    public string Script { get; set; } = "";

    /// <summary>The user explicitly accepted that this command shadows an existing one.</summary>
    public bool AllowOverride { get; set; }

    public QuickCommand Clone() => (QuickCommand)MemberwiseClone();
}

public sealed class CommandCollection
{
    public int Version { get; set; } = 1;
    public List<QuickCommand> Commands { get; set; } = [];
}

public sealed class AppSettings
{
    public bool SetupCompleted { get; set; }

    /// <summary>Folder holding commands.json. Null means the default folder (~/.quickcommands).</summary>
    public string? DataDirectory { get; set; }

    public List<ShellTarget> EnabledShells { get; set; } = [];

    /// <summary>UI language: null = system language, otherwise "de" or "en".</summary>
    public string? UiLanguage { get; set; }
}
