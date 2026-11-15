namespace QuickCommands.Core;

/// <summary>All file system locations the tool reads or writes. Injectable so tests can use a temp folder.</summary>
public sealed class QcPaths(string homeDirectory, string documentsDirectory, string configDirectory)
{
    public static QcPaths ForCurrentUser() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickCommands"));

    public string Home { get; } = homeDirectory;
    public string Documents { get; } = documentsDirectory;

    /// <summary>Machine-local folder for generated output (always under the home directory).</summary>
    public string OutputDirectory => Path.Combine(Home, ".quickcommands");
    public string BinDirectory => Path.Combine(OutputDirectory, "bin");
    public string PowerShellScript => Path.Combine(OutputDirectory, "qc.ps1");
    public string ShScript => Path.Combine(OutputDirectory, "qc.sh");

    /// <summary>Hash of the current qc.ps1 / qc.sh – open sessions compare it to reload automatically.</summary>
    public string PowerShellStamp => PowerShellScript + ".stamp";
    public string ShStamp => ShScript + ".stamp";

    /// <summary>Scratch folder for "Try it" runs.</summary>
    public string TryDirectory => Path.Combine(OutputDirectory, "try");

    public string DefaultDataDirectory => OutputDirectory;
    public string SettingsFile => Path.Combine(configDirectory, "settings.json");

    public string ResolveDataDirectory(AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.DataDirectory) ? DefaultDataDirectory : settings.DataDirectory;

    public string ProfilePath(ShellTarget target) => target switch
    {
        ShellTarget.Pwsh => OperatingSystem.IsWindows()
            ? Path.Combine(Documents, "PowerShell", "Microsoft.PowerShell_profile.ps1")
            : Path.Combine(Home, ".config", "powershell", "Microsoft.PowerShell_profile.ps1"),
        ShellTarget.WindowsPowerShell => Path.Combine(Documents, "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1"),
        ShellTarget.Bash => Path.Combine(Home, ".bashrc"),
        ShellTarget.Zsh => Path.Combine(Home, ".zshrc"),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "cmd has no profile file."),
    };

    /// <summary>How sh code refers to qc.sh: via $HOME when possible, so the line works in Git Bash and on Unix alike.</summary>
    public string ShScriptReference => ShReference(ShScript);

    public string ShStampReference => ShReference(ShStamp);

    /// <summary>A double-quoted sh word for a file under the home directory ("$HOME/…"), or its absolute sh path.</summary>
    public string ShReference(string file)
    {
        var relative = Path.GetRelativePath(Home, file);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? "\"" + PathUtil.ToShPath(file) + "\""
            : "\"$HOME/" + relative.Replace('\\', '/') + "\"";
    }
}

public static class PathUtil
{
    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string Normalize(string path)
    {
        var p = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        try
        {
            p = Path.GetFullPath(p);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Broken PATH entries are compared as-is.
        }
        return Path.TrimEndingDirectorySeparator(p);
    }

    public static bool AreEqual(string a, string b) => string.Equals(Normalize(a), Normalize(b), Comparison);

    /// <summary>C:\Users\x → /c/Users/x (Git Bash / MSYS style). Unix paths are returned unchanged.</summary>
    public static string ToShPath(string path)
    {
        if (path.Length >= 2 && path[1] == ':' && char.IsLetter(path[0]))
            return "/" + char.ToLowerInvariant(path[0]) + path[2..].Replace('\\', '/');
        return path.Replace('\\', '/');
    }
}
