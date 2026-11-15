namespace QuickCommands.Core;

/// <summary>Finds out which shells are installed on this machine.</summary>
public static class ShellDetector
{
    public static IReadOnlyList<ShellTarget> SupportedTargets { get; } =
        Enum.GetValues<ShellTarget>().Where(t => t.IsSupportedOnCurrentOs()).ToList();

    public static bool IsAvailable(ShellTarget target) => target switch
    {
        ShellTarget.Pwsh => FindExecutable("pwsh") is not null,
        ShellTarget.WindowsPowerShell => OperatingSystem.IsWindows() && FindExecutable("powershell") is not null,
        ShellTarget.Cmd => OperatingSystem.IsWindows(),
        ShellTarget.Bash => OperatingSystem.IsWindows() ? FindGitBash() is not null : FindExecutable("bash") is not null,
        ShellTarget.Zsh => !OperatingSystem.IsWindows() && FindExecutable("zsh") is not null,
        _ => false,
    };

    /// <summary>Sensible preselection for the first-run wizard.</summary>
    public static bool IsDefaultChoice(ShellTarget target)
    {
        if (OperatingSystem.IsWindows())
            return target is ShellTarget.Pwsh or ShellTarget.Cmd;
        if (OperatingSystem.IsMacOS())
            return target is ShellTarget.Zsh;
        return target is ShellTarget.Bash;
    }

    public static string? FindGitBash()
    {
        string[] candidates =
        [
            Path.Combine(ProgramFiles, "Git", "bin", "bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "bin", "bash.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Looks in PATH and in the usual install folders. Apps started from the macOS Finder get a minimal PATH
    /// without Homebrew, so those folders are searched explicitly.
    /// </summary>
    public static string? FindExecutable(string name)
    {
        var file = OperatingSystem.IsWindows() && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name + ".exe" : name;
        foreach (var dir in SearchDirectories())
        {
            var candidate = Path.Combine(dir, file);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static IEnumerable<string> SearchDirectories()
    {
        foreach (var dir in PathEnvironment.Split(Environment.GetEnvironmentVariable("PATH") ?? ""))
            yield return dir;
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(ProgramFiles, "PowerShell", "7");
            yield return Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
        }
        else
        {
            yield return "/opt/homebrew/bin";
            yield return "/usr/local/bin";
            yield return "/usr/bin";
            yield return "/bin";
            yield return "/snap/bin";
            yield return "/usr/local/microsoft/powershell/7";
            yield return "/opt/microsoft/powershell/7";
        }
    }

    private static string ProgramFiles =>
        Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
}
