using System.ComponentModel;
using System.Diagnostics;

namespace QuickCommands.Core;

/// <summary>The shell command line that runs a trial – without the terminal window around it.</summary>
public sealed record TrialCommand(string FileName, IReadOnlyList<string> Arguments, string Title);

/// <summary>
/// "Try it": runs the current editor state of a command in a new terminal window, on top of the user's normal
/// profile, without saving anything or touching the generated files.
/// </summary>
public sealed class TrialRunner(QcPaths paths)
{
    /// <summary>Writes the trial files and returns the shell to start. Throws if no matching shell is installed.</summary>
    public TrialCommand Prepare(QuickCommand command, string arguments, TerminalTexts texts, bool preferZsh = false, bool preferWindowsPowerShell = false)
    {
        var dir = paths.TryDirectory;
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        var call = (command.Name + " " + arguments.Trim()).Trim();
        var title = "QuickCommands: " + command.Name;
        switch (command.Language)
        {
            case ShellLanguage.PowerShell:
            {
                var shell = (preferWindowsPowerShell ? null : ShellDetector.FindExecutable("pwsh"))
                            ?? (OperatingSystem.IsWindows() ? ShellDetector.FindExecutable("powershell") : null)
                            ?? ShellDetector.FindExecutable("pwsh")
                            ?? throw new FileNotFoundException("PowerShell (pwsh) was not found.");
                var file = Path.Combine(dir, "try.ps1");
                var script = PowerShellGenerator.GenerateSingle(command, texts) +
                             "Write-Host ''\n" +
                             "Write-Host " + PowerShellGenerator.Quote("> " + call) + " -ForegroundColor Cyan\n" +
                             call + "\n";
                FileUtil.WriteAtomic(file, script, FileUtil.Utf8Bom);
                return new(shell, ["-NoExit", "-ExecutionPolicy", "Bypass", "-File", file], title);
            }
            case ShellLanguage.Cmd:
            {
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException("cmd only exists on Windows.");
                var encoding = CmdGenerator.FileEncoding;
                var commandFile = Path.Combine(dir, command.Name + CmdGenerator.FileExtension);
                FileUtil.WriteAtomic(commandFile, CmdGenerator.GenerateCommandFile(command, texts), encoding);
                var run = Path.Combine(dir, "run.cmd");
                FileUtil.WriteAtomic(run,
                    "@echo off\r\necho(\r\necho(" + CmdGenerator.EscapeEcho("> " + call) + "\r\n" +
                    "call \"" + commandFile + "\" " + arguments.Trim() + "\r\n", encoding);
                return new("cmd.exe", ["/k", run], title);
            }
            default:
            {
                var definition = ShGenerator.GenerateSingle(command, texts) +
                                 "printf '%s\\n' '' " + ShGenerator.Quote("> " + call) + "\n" +
                                 call + "\n";
                var zsh = OperatingSystem.IsWindows() ? null : ShellDetector.FindExecutable("zsh");
                if (preferZsh && zsh is not null)
                {
                    // zsh has no --rcfile: a temporary ZDOTDIR loads the real config first.
                    FileUtil.WriteAtomic(Path.Combine(dir, ".zshenv"), "[ -f \"$HOME/.zshenv\" ] && . \"$HOME/.zshenv\"\n", FileUtil.Utf8NoBom);
                    FileUtil.WriteAtomic(Path.Combine(dir, ".zshrc"),
                        "ZDOTDIR=\"$HOME\"\n[ -f \"$HOME/.zshrc\" ] && . \"$HOME/.zshrc\"\n" + definition, FileUtil.Utf8NoBom);
                    return new("env", ["ZDOTDIR=" + dir, zsh, "-i"], title);
                }

                var bash = OperatingSystem.IsWindows() ? ShellDetector.FindGitBash() : ShellDetector.FindExecutable("bash");
                if (bash is null)
                    throw new FileNotFoundException(OperatingSystem.IsWindows() ? "Git Bash was not found." : "bash was not found.");
                var rc = Path.Combine(dir, "try.bashrc");
                FileUtil.WriteAtomic(rc,
                    "[ -f /etc/bash.bashrc ] && . /etc/bash.bashrc\n[ -f \"$HOME/.bashrc\" ] && . \"$HOME/.bashrc\"\n" + definition,
                    FileUtil.Utf8NoBom);
                return new(bash, ["--rcfile", PathUtil.ToShPath(rc), "-i"], title);
            }
        }
    }

    /// <summary>Opens a terminal window running the trial.</summary>
    public void Launch(TrialCommand trial)
    {
        if (OperatingSystem.IsWindows())
            LaunchWindows(trial);
        else if (OperatingSystem.IsMacOS())
            LaunchMac(trial);
        else
            LaunchLinux(trial);
    }

    private void LaunchWindows(TrialCommand trial)
    {
        var wt = ShellDetector.FindExecutable("wt");
        if (wt is not null)
        {
            Start(wt, ["new-tab", "--title", trial.Title, "--startingDirectory", paths.Home, trial.FileName, .. trial.Arguments], useShellExecute: false);
            return;
        }
        // A console program started via the shell gets its own console window.
        Start(trial.FileName, trial.Arguments, useShellExecute: true);
    }

    private void LaunchMac(TrialCommand trial)
    {
        var command = "cd " + ShGenerator.Quote(paths.Home) + " && exec " +
                      string.Join(' ', new[] { trial.FileName }.Concat(trial.Arguments).Select(ShGenerator.Quote));
        var appleScript = "\"" + command.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        Start("osascript", ["-e", "tell application \"Terminal\"", "-e", "activate", "-e", "do script " + appleScript, "-e", "end tell"], useShellExecute: false);
    }

    private void LaunchLinux(TrialCommand trial)
    {
        string[] argv = [trial.FileName, .. trial.Arguments];
        (string Exe, string[] Prefix)[] terminals =
        [
            ("x-terminal-emulator", ["-e"]),
            ("gnome-terminal", ["--"]),
            ("konsole", ["-e"]),
            ("xfce4-terminal", ["-x"]),
            ("kitty", []),
            ("alacritty", ["-e"]),
            ("wezterm", ["start", "--"]),
            ("foot", []),
            ("xterm", ["-e"]),
        ];
        foreach (var (exe, prefix) in terminals)
        {
            var path = ShellDetector.FindExecutable(exe);
            if (path is null)
                continue;
            Start(path, [.. prefix, .. argv], useShellExecute: false);
            return;
        }
        throw new FileNotFoundException("No terminal emulator was found (x-terminal-emulator, gnome-terminal, konsole, …).");
    }

    private void Start(string fileName, IEnumerable<string> arguments, bool useShellExecute)
    {
        var psi = new ProcessStartInfo(fileName) { UseShellExecute = useShellExecute, WorkingDirectory = paths.Home };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);
        try
        {
            using var _ = Process.Start(psi);
        }
        catch (Win32Exception e)
        {
            throw new FileNotFoundException($"{fileName}: {e.Message}", e);
        }
    }
}
