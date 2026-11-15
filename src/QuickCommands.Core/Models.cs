using System.Text.Json.Serialization;

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

    /// <summary>Declared parameters. Empty = the script parses $args / %* / "$@" itself (v0.1 behaviour).</summary>
    public List<CommandParameter> Parameters { get; set; } = [];

    [JsonIgnore]
    public bool HasParameters => Parameters.Count > 0;

    public QuickCommand Clone()
    {
        var copy = (QuickCommand)MemberwiseClone();
        copy.Parameters = Parameters.Select(p => p.Clone()).ToList();
        return copy;
    }
}

public enum ParameterKind
{
    /// <summary>Any value; positional or --name value.</summary>
    Text,

    /// <summary>Flag without value (--force).</summary>
    Switch,

    /// <summary>One of a fixed list of values.</summary>
    Choice,
}

public sealed class CommandParameter
{
    public string Name { get; set; } = "";
    public ParameterKind Kind { get; set; }
    public bool Required { get; set; }

    /// <summary>Default value for Text/Choice. Ignored for required parameters and switches.</summary>
    public string Default { get; set; } = "";

    public List<string> Choices { get; set; } = [];
    public string Description { get; set; } = "";

    /// <summary>Text and choice parameters can be given by position (in declaration order); switches only by flag.</summary>
    [JsonIgnore]
    public bool IsPositional => Kind != ParameterKind.Switch;

    /// <summary>The default that actually applies (none for switches and required parameters).</summary>
    [JsonIgnore]
    public string EffectiveDefault => Kind == ParameterKind.Switch || Required ? "" : Default;

    public CommandParameter Clone() => new()
    {
        Name = Name,
        Kind = Kind,
        Required = Required,
        Default = Default,
        Choices = [.. Choices],
        Description = Description,
    };
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

    /// <summary>UI language: null = system language, otherwise "de" or "en". Also used for the terminal output.</summary>
    public string? UiLanguage { get; set; }

    /// <summary>Open PowerShell/bash/zsh sessions pick up saved changes without "qc reload".</summary>
    public bool AutoReload { get; set; } = true;
}
