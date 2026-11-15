using QuickCommands.Core;

namespace QuickCommands.Tests;

/// <summary>Declared parameters, help, `qc name` and auto-reload – run in the real shells.</summary>
public class ParameterExecutionTests
{
    private static QuickCommand Deploy(ShellLanguage language, string script) => new()
    {
        Name = "deploy",
        Language = language,
        Description = "Deploy something",
        Script = script,
        Parameters =
        [
            new() { Name = "name", Kind = ParameterKind.Text, Required = true, Description = "App name" },
            new() { Name = "target", Kind = ParameterKind.Choice, Choices = ["dev", "prod"], Default = "dev" },
            new() { Name = "port", Kind = ParameterKind.Text, Default = "5173" },
            new() { Name = "force", Kind = ParameterKind.Switch, Description = "No questions" },
        ],
    };

    private static readonly GeneratorOptions German = new(TerminalTexts.German, AutoReload: true);

    // ---------- PowerShell ----------

    [Fact]
    public async Task PowerShell_parameters_bind_by_position_by_flag_and_natively()
    {
        Assert.SkipWhen(ExecutionTests.Pwsh is null, "pwsh not installed");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        new OutputWriter(paths).WriteAll([Deploy(ShellLanguage.PowerShell,
            "Write-Output \"name=$name target=$target port=$port force=$force rest=$($args -join ',')\"")]);

        var output = await ExecutionTests.RunPwsh(paths,
            "deploy app; deploy app prod --port 8080 --force extra --x; deploy -name n2 -port:9000 -target prod; deploy --port=1 --name=n3");

        Assert.Contains("name=app target=dev port=5173 force=False rest=", output);
        Assert.Contains("name=app target=prod port=8080 force=True rest=extra,--x", output);
        Assert.Contains("name=n2 target=prod port=9000 force=False rest=", output);
        Assert.Contains("name=n3 target=dev port=1 force=False rest=", output);
    }

    [Fact]
    public async Task PowerShell_reports_missing_and_invalid_arguments_and_prints_help()
    {
        Assert.SkipWhen(ExecutionTests.Pwsh is null, "pwsh not installed");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        new OutputWriter(paths).WriteAll([Deploy(ShellLanguage.PowerShell, "Write-Output 'ran'")], German);

        var output = await ExecutionTests.RunPwsh(paths, "deploy; deploy app staging; deploy --help; qc deploy; qc nope");

        Assert.DoesNotContain("ran", output);
        Assert.Contains("deploy: Argument <name> fehlt", output);
        Assert.Contains("Aufruf: deploy <name> [<target>] [<port>] [--force]", output);
        Assert.Contains("deploy: <target> muss einer dieser Werte sein: dev, prod", output);
        Assert.Contains("<name>, --name <wert>", output);
        Assert.Contains("deploy (powershell) - Deploy something", output);
        Assert.Contains("qc: unbekannter Command: nope", output);
    }

    [Fact]
    public async Task PowerShell_completion_offers_flags_and_choice_values()
    {
        Assert.SkipWhen(ExecutionTests.Pwsh is null, "pwsh not installed");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        new OutputWriter(paths).WriteAll([Deploy(ShellLanguage.PowerShell, "'x'")]);

        var output = await ExecutionTests.RunPwsh(paths,
            "(TabExpansion2 -inputScript 'deploy --t' -cursorColumn 10).CompletionMatches.CompletionText; " +
            "(TabExpansion2 -inputScript 'deploy --target p' -cursorColumn 17).CompletionMatches.CompletionText");

        Assert.Contains("--target", output);
        Assert.Contains("prod", output);
    }

    [Fact]
    public async Task PowerShell_session_reloads_changed_and_new_commands_automatically()
    {
        Assert.SkipWhen(ExecutionTests.Pwsh is null, "pwsh not installed");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        new OutputWriter(paths).WriteAll([Make.Command("first", ShellLanguage.PowerShell, "'one'")]);

        var next = PowerShellGenerator.GenerateWithStamp(
            [Make.Command("first", ShellLanguage.PowerShell, "'one-changed'"), Make.Command("second", ShellLanguage.PowerShell, "'two'")],
            paths.PowerShellScript, paths.PowerShellStamp, GeneratorOptions.Default);
        var rewrite = $"[IO.File]::WriteAllText({PowerShellGenerator.Quote(paths.PowerShellScript)}, {PowerShellGenerator.Quote(next.Text)}); " +
                      $"[IO.File]::WriteAllText({PowerShellGenerator.Quote(paths.PowerShellStamp)}, {PowerShellGenerator.Quote(next.Stamp)})";

        var output = await ExecutionTests.RunPwsh(paths, $"first; {rewrite}; first; second");

        Assert.Contains("one\n", output);
        Assert.Contains("one-changed", output);
        Assert.Contains("two", output);
    }

    // ---------- cmd ----------

    [Fact]
    public async Task Cmd_parameters_bind_and_stay_local_while_cd_is_kept()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "cmd only exists on Windows");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        var target = Directory.CreateDirectory(Path.Combine(tmp.Path, "cmd target")).FullName;
        new OutputWriter(paths).WriteAll(
        [
            Deploy(ShellLanguage.Cmd, "echo name=%name% target=%target% port=%port% force=%force%\r\nset leaked=1"),
            new()
            {
                Name = "go", Language = ShellLanguage.Cmd, Script = "cd /d \"%dir%\"\r\nexit /b 3",
                Parameters = [new() { Name = "dir", Required = true }],
            },
        ]);
        var deploy = Path.Combine(paths.BinDirectory, "deploy.cmd");
        var go = Path.Combine(paths.BinDirectory, "go.cmd");

        var a = await ExecutionTests.RunCmd("call", deploy, "app");
        // %vars% on the cmd /c line would be expanded before the call – a wrapper batch file checks afterwards.
        var wrapper = Path.Combine(tmp.Path, "wrapper.cmd");
        File.WriteAllLines(wrapper, ["@echo off", $"call \"{deploy}\" app prod --port 8080 --force", "echo [%leaked%][%name%]"]);
        var b = await ExecutionTests.RunCmd("call", wrapper);
        var c = await ExecutionTests.RunCmd("call", deploy, "--name=n3", "--port=1");
        var d = await ExecutionTests.RunCmd("call", go, target, "&", "if", "errorlevel", "3", "echo", "rc3", "&", "cd");

        Assert.Contains("name=app target=dev port=5173 force=", a);
        Assert.Contains("name=app target=prod port=8080 force=1", b);
        Assert.DoesNotContain("[1]", b);
        Assert.DoesNotContain("[app]", b);
        Assert.Contains("name=n3 target=dev port=1 force=", c);
        Assert.Contains("rc3", d);
        Assert.Contains(target, d);
    }

    [Fact]
    public async Task Cmd_reports_errors_prints_help_and_qc_details()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "cmd only exists on Windows");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        new OutputWriter(paths).WriteAll([Deploy(ShellLanguage.Cmd, "echo ran")], German);
        var deploy = Path.Combine(paths.BinDirectory, "deploy.cmd");
        var qc = Path.Combine(paths.BinDirectory, "qc.cmd");

        var missing = await ExecutionTests.RunCmd("call", deploy);
        var invalid = await ExecutionTests.RunCmd("call", deploy, "app", "staging");
        var help = await ExecutionTests.RunCmd("call", deploy, "--help");
        var details = await ExecutionTests.RunCmd("call", qc, "deploy");
        var unknown = await ExecutionTests.RunCmd("call", qc, "no&pe x");

        Assert.Contains("deploy: Argument <name> fehlt", missing);
        Assert.DoesNotContain("ran", missing);
        Assert.Contains("deploy: <target> muss einer dieser Werte sein: dev, prod", invalid);
        Assert.Contains("Aufruf: deploy <name> [<target>] [<port>] [--force]", help);
        Assert.Contains("deploy (cmd) - Deploy something", details);
        Assert.Contains("qc: unbekannter Command: no&pe x", unknown);
    }

    // ---------- sh ----------

    [Fact]
    public async Task Sh_parameters_bind_and_report_errors()
    {
        Assert.SkipWhen(ExecutionTests.Bash is null, "bash / Git Bash not installed");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        new OutputWriter(paths).WriteAll([Deploy(ShellLanguage.Sh, "echo \"name=$name target=$target port=$port force=$force rest=$*\"")], German);

        var output = await ExecutionTests.RunBash(paths,
            "deploy app; deploy app prod --port 8080 --force extra --x; deploy --port=1 --name=n3; " +
            "deploy; echo \"rc=$?\"; deploy app staging; deploy --help; qc deploy; qc nope");

        Assert.Contains("name=app target=dev port=5173 force= rest=\n", output);
        Assert.Contains("name=app target=prod port=8080 force=1 rest=extra --x", output);
        Assert.Contains("name=n3 target=dev port=1 force= rest=", output);
        Assert.Contains("deploy: Argument <name> fehlt", output);
        Assert.Contains("rc=2", output);
        Assert.Contains("deploy: <target> muss einer dieser Werte sein: dev, prod", output);
        Assert.Contains("Aufruf: deploy <name> [<target>] [<port>] [--force]", output);
        Assert.Contains("deploy (sh) - Deploy something", output);
        Assert.Contains("qc: unbekannter Command: nope", output);
    }

    [Fact]
    public async Task Sh_completion_offers_flags_and_choice_values()
    {
        Assert.SkipWhen(ExecutionTests.Bash is null, "bash / Git Bash not installed");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        new OutputWriter(paths).WriteAll([Deploy(ShellLanguage.Sh, "true")]);

        var output = await ExecutionTests.RunBash(paths,
            "complete -p deploy; COMP_WORDS=(deploy --t); COMP_CWORD=1; __qc_c0; echo \"[${COMPREPLY[*]}]\"; " +
            "COMP_WORDS=(deploy --target p); COMP_CWORD=2; __qc_c0; echo \"[${COMPREPLY[*]}]\"");

        Assert.Contains("complete -o default -F __qc_c0 deploy", output);
        Assert.Contains("[--target]", output);
        Assert.Contains("[prod]", output);
    }

    [Fact]
    public async Task Sh_autoreload_hooks_into_the_prompt_and_reloads_changes()
    {
        Assert.SkipWhen(ExecutionTests.Bash is null, "bash / Git Bash not installed");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        new OutputWriter(paths).WriteAll([Make.Command("first", ShellLanguage.Sh, "echo one")]);

        var next = Path.Combine(tmp.Path, "next");
        new OutputWriter(new QcPaths(next, next, next)).WriteAll(
            [Make.Command("first", ShellLanguage.Sh, "echo one-changed"), Make.Command("second", ShellLanguage.Sh, "echo two")]);
        // Both files reference $HOME/.quickcommands/qc.sh, so they can be copied over.
        var src = ShGenerator.Quote(PathUtil.ToShPath(Path.Combine(next, ".quickcommands")));

        var output = await ExecutionTests.RunBash(paths,
            "PROMPT_COMMAND='echo prompt'; . \"$HOME/.quickcommands/qc.sh\"; printf '[%s]\\n' \"$PROMPT_COMMAND\"; first; __qc_autoreload; first; " +
            $"cp {src}/qc.sh {src}/qc.sh.stamp \"$HOME/.quickcommands/\"; false; __qc_autoreload; echo \"rc=$?\"; first; second");

        Assert.Contains("[__qc_autoreload\necho prompt]", output);
        Assert.Contains("one\none\n", output);
        Assert.Contains("rc=1", output);
        Assert.Contains("one-changed", output);
        Assert.Contains("two", output);
    }
}

/// <summary>The same sh file in zsh (macOS default shell). Skipped where zsh is not installed (e.g. Windows).</summary>
public class ZshExecutionTests
{
    private static string? Zsh => OperatingSystem.IsWindows() ? null : ShellDetector.FindExecutable("zsh");

    private static async Task<string> RunZsh(QcPaths paths, string script)
    {
        var env = new Dictionary<string, string> { ["HOME"] = paths.Home };
        var run = await ProcessRunner.RunAsync(Zsh!, ["-f", "-c", $". \"$HOME/.quickcommands/qc.sh\"; {script}"],
            TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken, env);
        return (run.StdOut + run.StdErr).ReplaceLineEndings("\n");
    }

    [Fact]
    public async Task Zsh_runs_commands_with_parameters_and_qc()
    {
        Assert.SkipWhen(Zsh is null, "zsh not installed");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        var target = Directory.CreateDirectory(Path.Combine(tmp.Path, "zsh target")).FullName;
        new OutputWriter(paths).WriteAll(
        [
            new()
            {
                Name = "deploy", Language = ShellLanguage.Sh, Description = "Deploy",
                Script = "echo \"name=$name target=$target force=$force rest=$*\"",
                Parameters =
                [
                    new() { Name = "name", Required = true },
                    new() { Name = "target", Kind = ParameterKind.Choice, Choices = ["dev", "prod"], Default = "dev" },
                    new() { Name = "force", Kind = ParameterKind.Switch },
                ],
            },
            Make.Command("go", ShellLanguage.Sh, "cd \"$1\""),
        ]);

        var output = await RunZsh(paths,
            $"deploy app prod --force x; deploy; deploy app nope; go {ShGenerator.Quote(target)}; pwd; qc; " +
            "printf '[%s]\n' \"${precmd_functions[@]}\"");

        Assert.Contains("name=app target=prod force=1 rest=x", output);
        Assert.Contains("deploy: missing argument <name>", output);
        Assert.Contains("deploy: <target> must be one of: dev, prod", output);
        Assert.Contains(target, output);
        Assert.Contains("QuickCommands (sh)", output);
        Assert.Contains("[__qc_autoreload]", output);
    }
}
