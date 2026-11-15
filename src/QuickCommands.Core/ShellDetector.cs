namespace QuickCommands.Core;

/// <summary>Finds out which shells are installed on this machine.</summary>
public static class ShellDetector
{
    public static IReadOnlyList<ShellTarget> SupportedTargets { get; } =
        Enum.GetValues<ShellTarget>().Where(t => t.IsSupportedOnCurrentOs()).ToList();

    public static bool IsAvailable(ShellTarget target) => target switch
    {
        ShellTarget.Pwsh => FindOnPath("pwsh") is not null
                            || (OperatingSystem.IsWindows() && File.Exists(Path.Combine(ProgramFiles, "PowerShell", "7", "pwsh.exe"))),
        ShellTarget.WindowsPowerShell => OperatingSystem.IsWindows()
                                         && File.Exists(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")),
        ShellTarget.Cmd => OperatingSystem.IsWindows(),
        ShellTarget.Bash => OperatingSystem.IsWindows() ? FindGitBash() is not null : FindOnPath("bash") is not null,
        ShellTarget.Zsh => !OperatingSystem.IsWindows() && FindOnPath("zsh") is not null,
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

    private static string ProgramFiles =>
        Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    private static string? FindOnPath(string name)
    {
        var names = OperatingSystem.IsWindows() ? new[] { name + ".exe" } : [name];
        foreach (var dir in PathEnvironment.Split(Environment.GetEnvironmentVariable("PATH") ?? ""))
        {
            foreach (var n in names)
            {
                var candidate = Path.Combine(dir, n);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }
}
