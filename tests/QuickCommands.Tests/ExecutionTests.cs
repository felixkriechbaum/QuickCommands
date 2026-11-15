using QuickCommands.Core;

namespace QuickCommands.Tests;

/// <summary>
/// Runs the generated files in the real shells. These are the tests that prove `cd` really changes the
/// directory and that the escaping works – string comparisons alone cannot show that.
/// </summary>
public class ExecutionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private const string GitBash = @"C:\Program Files\Git\bin\bash.exe";

    private static string? Pwsh => FindOnPath(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh");

    private static string? Bash => OperatingSystem.IsWindows()
        ? (File.Exists(GitBash) ? GitBash : null)
        : FindOnPath("bash");

    private static string? FindOnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.Combine(d.Trim(), exe))
            .FirstOrDefault(File.Exists);

    private static async Task<string> RunPwsh(QcPaths paths, string script)
    {
        var run = await ProcessRunner.RunAsync(Pwsh!,
            ["-NoProfile", "-NonInteractive", "-Command", $". {PowerShellGenerator.Quote(paths.PowerShellScript)}; {script}"],
            Timeout, TestContext.Current.CancellationToken);
        return (run.StdOut + run.StdErr).ReplaceLineEndings("\n");
    }

    private static async Task<string> RunCmd(params string[] args)
    {
        var run = await ProcessRunner.RunAsync("cmd.exe", ["/d", "/c", .. args], Timeout, TestContext.Current.CancellationToken);
        return (run.StdOut + run.StdErr).ReplaceLineEndings("\n");
    }

    private static async Task<string> RunBash(QcPaths paths, string script)
    {
        var env = new Dictionary<string, string> { ["HOME"] = PathUtil.ToShPath(paths.Home) };
        var run = await ProcessRunner.RunAsync(Bash!,
            ["--noprofile", "--norc", "-c", $". \"$HOME/.quickcommands/qc.sh\"; {script}"],
            Timeout, TestContext.Current.CancellationToken, env);
        return (run.StdOut + run.StdErr).ReplaceLineEndings("\n");
    }

    // ---------- PowerShell ----------

    [Fact]
    public async Task PowerShell_command_receives_arguments()
    {
        Assert.SkipWhen(Pwsh is null, "pwsh not installed");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        new OutputWriter(paths).WriteAll([Make.Command("greet", ShellLanguage.PowerShell, "Write-Output (\"Hello \" + $args[0] + \" it's me\")")]);

        Assert.Contains("Hello world it's me", await RunPwsh(paths, "greet world"));
    }

    [Fact]
    public async Task PowerShell_command_can_change_the_directory_and_override_an_alias()
    {
        Assert.SkipWhen(Pwsh is null, "pwsh not installed");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        var target = Directory.CreateDirectory(Path.Combine(tmp.Path, "target dir")).FullName;
        new OutputWriter(paths).WriteAll(
        [
            Make.Command("go", ShellLanguage.PowerShell, "Set-Location -LiteralPath $args[0]"),
            Make.Command("ls", ShellLanguage.PowerShell, "Write-Output 'custom ls'"),
        ]);

        var output = await RunPwsh(paths, $"go {PowerShellGenerator.Quote(target)}; (Get-Location).Path; ls");

        Assert.Contains(target, output);
        Assert.Contains("custom ls", output);
    }

    [Fact]
    public async Task PowerShell_syntax_error_in_one_command_does_not_break_the_others()
    {
        Assert.SkipWhen(Pwsh is null, "pwsh not installed");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        new OutputWriter(paths).WriteAll(
        [
            Make.Command("broken", ShellLanguage.PowerShell, "if ( {"),
            Make.Command("works", ShellLanguage.PowerShell, "Write-Output 'still working'"),
        ]);

        var output = await RunPwsh(paths, "works");

        Assert.Contains("'broken' could not be loaded", output);
        Assert.Contains("still working", output);
    }

    [Fact]
    public async Task PowerShell_qc_lists_commands_and_reload_drops_deleted_ones()
    {
        Assert.SkipWhen(Pwsh is null, "pwsh not installed");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        var writer = new OutputWriter(paths);
        writer.WriteAll(
        [
            Make.Command("first", ShellLanguage.PowerShell, "'one'", "Erster Command"),
            Make.Command("viacmd", ShellLanguage.Cmd, "echo x", "nur in cmd"),
        ]);

        // Simulate saving in the app while the terminal is open: rewrite the file, then `qc reload`.
        var rewrite = $"[IO.File]::WriteAllText({PowerShellGenerator.Quote(paths.PowerShellScript)}, " +
                      $"{PowerShellGenerator.Quote(PowerShellGenerator.Generate([Make.Command("second", ShellLanguage.PowerShell, "'two'")], paths.PowerShellScript))})";
        var output = await RunPwsh(paths,
            $"qc; qc -a; {rewrite}; qc reload; \"first exists: $([bool](Get-Command first -ErrorAction SilentlyContinue))\"; second");

        Assert.Contains("first     Erster Command", output);
        Assert.Contains("viacmd    cmd         nur in cmd", output);
        Assert.Contains("QuickCommands reloaded (1 commands).", output);
        Assert.Contains("first exists: False", output);
        Assert.Contains("two", output);
    }

    // ---------- cmd ----------

    [Fact]
    public async Task Cmd_command_receives_arguments_and_keeps_the_directory_change()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "cmd only exists on Windows");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        var target = Directory.CreateDirectory(Path.Combine(tmp.Path, "cmdtarget")).FullName;
        new OutputWriter(paths).WriteAll(
        [
            Make.Command("greet", ShellLanguage.Cmd, "echo Hello %1"),
            Make.Command("go", ShellLanguage.Cmd, "cd /d \"%~1\""),
        ]);
        var bin = paths.BinDirectory;

        var greet = await RunCmd("call", Path.Combine(bin, "greet.cmd"), "world");
        var go = await RunCmd("call", Path.Combine(bin, "go.cmd"), target, "&", "cd");

        Assert.Contains("Hello world", greet);
        Assert.DoesNotContain("@echo off", greet);
        Assert.Contains(target, go);
    }

    [Fact]
    public async Task Cmd_qc_lists_descriptions_with_special_characters()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "cmd only exists on Windows");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        new OutputWriter(paths).WriteAll(
        [
            Make.Command("tricky", ShellLanguage.Cmd, "echo x", "a & b | c > d 100% \"quoted\""),
            Make.Command("psonly", ShellLanguage.PowerShell, "'x'"),
        ]);
        var qc = Path.Combine(paths.BinDirectory, "qc.cmd");

        var list = await RunCmd("call", qc);
        var all = await RunCmd("call", qc, "-a");
        var reload = await RunCmd("call", qc, "reload");

        Assert.Contains("tricky    a & b | c > d 100% 'quoted'", list);
        Assert.DoesNotContain("psonly", list);
        Assert.Contains("psonly", all);
        Assert.Contains("available immediately", reload);
    }

    // ---------- sh (bash / Git Bash) ----------

    [Fact]
    public async Task Sh_command_receives_arguments_and_changes_the_directory()
    {
        Assert.SkipWhen(Bash is null, "bash / Git Bash not installed");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        var target = Directory.CreateDirectory(Path.Combine(tmp.Path, "sh target")).FullName;
        new OutputWriter(paths).WriteAll(
        [
            Make.Command("greet", ShellLanguage.Sh, "echo \"Hello $1, it's me\""),
            Make.Command("go", ShellLanguage.Sh, "cd \"$1\""),
        ]);

        var output = await RunBash(paths, $"greet world; go {ShGenerator.Quote(PathUtil.ToShPath(target))}; pwd");

        Assert.Contains("Hello world, it's me", output);
        Assert.Contains("sh target", output);
    }

    [Fact]
    public async Task Sh_syntax_error_in_one_command_does_not_break_the_others()
    {
        Assert.SkipWhen(Bash is null, "bash / Git Bash not installed");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        new OutputWriter(paths).WriteAll(
        [
            Make.Command("broken", ShellLanguage.Sh, "if then fi ((("),
            Make.Command("works", ShellLanguage.Sh, "echo still working"),
        ]);

        var output = await RunBash(paths, "works");

        Assert.Contains("'broken' could not be loaded", output);
        Assert.Contains("still working", output);
    }

    [Fact]
    public async Task Sh_qc_lists_and_reload_drops_deleted_commands()
    {
        Assert.SkipWhen(Bash is null, "bash / Git Bash not installed");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        new OutputWriter(paths).WriteAll([Make.Command("first", ShellLanguage.Sh, "echo one", "Erster")]);

        var rewritten = ShGenerator.Generate([Make.Command("second", ShellLanguage.Sh, "echo two")], paths.ShScriptReference);
        var tmpFile = Path.Combine(tmp.Path, "next.sh");
        File.WriteAllText(tmpFile, rewritten);
        var output = await RunBash(paths,
            $"qc; cp {ShGenerator.Quote(PathUtil.ToShPath(tmpFile))} \"$HOME/.quickcommands/qc.sh\"; qc reload; " +
            "type first >/dev/null 2>&1 && echo 'first still there' || echo 'first gone'; second");

        Assert.Contains("first     Erster", output);
        Assert.Contains("QuickCommands reloaded.", output);
        Assert.Contains("first gone", output);
        Assert.Contains("two", output);
    }
}
