using QuickCommands.Core;

namespace QuickCommands.Tests;

public class ParameterRuleTests
{
    private static IReadOnlyList<ParameterIssueKind> Kinds(ShellLanguage language, string script, params CommandParameter[] ps) =>
        ParameterRules.Validate(language, ps, script).Select(i => i.Kind).ToList();

    [Fact]
    public void Valid_parameters_have_no_issues() =>
        Assert.Empty(Kinds(ShellLanguage.Sh, "",
            new() { Name = "name", Required = true },
            new() { Name = "env", Kind = ParameterKind.Choice, Choices = ["dev", "prod"], Default = "dev" },
            new() { Name = "force", Kind = ParameterKind.Switch }));

    [Theory]
    [InlineData("", ParameterIssueKind.EmptyName)]
    [InlineData("1abc", ParameterIssueKind.InvalidName)]
    [InlineData("my-name", ParameterIssueKind.InvalidName)]
    [InlineData("help", ParameterIssueKind.ReservedName)]
    public void Bad_names_are_reported(string name, ParameterIssueKind expected) =>
        Assert.Contains(expected, Kinds(ShellLanguage.Sh, "", new CommandParameter { Name = name }));

    [Theory]
    [InlineData(ShellLanguage.Cmd, "path")]
    [InlineData(ShellLanguage.PowerShell, "home")]
    [InlineData(ShellLanguage.PowerShell, "args")]
    [InlineData(ShellLanguage.Sh, "path")]
    public void Shell_variables_are_blocked_per_language(ShellLanguage language, string name) =>
        Assert.Contains(ParameterIssueKind.ShellVariable, Kinds(language, "", new CommandParameter { Name = name }));

    [Fact]
    public void Home_is_fine_in_sh_and_duplicates_are_reported()
    {
        Assert.Empty(Kinds(ShellLanguage.Sh, "", new CommandParameter { Name = "home" }));
        Assert.Contains(ParameterIssueKind.DuplicateName, Kinds(ShellLanguage.Sh, "", new() { Name = "a" }, new() { Name = "A" }));
    }

    [Fact]
    public void Choice_rules()
    {
        Assert.Contains(ParameterIssueKind.NoChoices, Kinds(ShellLanguage.Sh, "", new CommandParameter { Name = "e", Kind = ParameterKind.Choice }));
        Assert.Contains(ParameterIssueKind.InvalidChoice, Kinds(ShellLanguage.Sh, "", new CommandParameter { Name = "e", Kind = ParameterKind.Choice, Choices = ["a b'"] }));
        Assert.Contains(ParameterIssueKind.DefaultNotInChoices, Kinds(ShellLanguage.Sh, "", new CommandParameter { Name = "e", Kind = ParameterKind.Choice, Choices = ["a"], Default = "b" }));
        Assert.Equal(["dev", "prod", "test"], ParameterRules.ParseChoices(" dev | prod,test dev"));
    }

    [Fact]
    public void Defaults_with_quotes_are_rejected_but_ignored_when_required()
    {
        Assert.Contains(ParameterIssueKind.InvalidDefault, Kinds(ShellLanguage.Cmd, "", new CommandParameter { Name = "x", Default = "a\"b" }));
        Assert.Empty(Kinds(ShellLanguage.Cmd, "", new CommandParameter { Name = "x", Default = "a\"b", Required = true }));
    }

    [Theory]
    [InlineData("param($x)\n$x", true)]
    [InlineData("# comment\n<# help #>\n[CmdletBinding()]\nParam (\n $x)", true)]
    [InlineData("Write-Output 'param('", false)]
    public void PowerShell_param_block_is_detected(string script, bool expected) =>
        Assert.Equal(expected, Kinds(ShellLanguage.PowerShell, script, new CommandParameter { Name = "x" }).Contains(ParameterIssueKind.ScriptHasParamBlock));

    [Fact]
    public void Help_text_lists_usage_and_parameters()
    {
        var c = new QuickCommand
        {
            Name = "serve", Description = "Start a server", Language = ShellLanguage.Sh,
            Parameters =
            [
                new() { Name = "port", Default = "8000", Description = "Port" },
                new() { Name = "env", Kind = ParameterKind.Choice, Choices = ["dev", "prod"], Required = true },
                new() { Name = "open", Kind = ParameterKind.Switch, Description = "Open browser" },
            ],
        };

        var lines = HelpText.Lines(c, TerminalTexts.English, includeLanguage: true);

        Assert.Equal("serve (sh) - Start a server", lines[0]);
        Assert.Equal("  Usage: serve [<port>] <env> [--open]", lines[1]);
        Assert.Contains("    <port>, --port <value>  Port (default: 8000)", lines);
        Assert.Contains("    <env>, --env dev|prod   (required)", lines);
        Assert.Contains("    --open                  Open browser", lines);
    }
}

public class HistoryTests
{
    [Fact]
    public void Every_save_is_recorded_once_and_the_first_save_keeps_the_previous_state()
    {
        using var tmp = new TempDir();
        var repo = new CommandRepository(tmp.Path);
        File.WriteAllText(repo.FilePath, "{\"version\":1,\"commands\":[]}");

        var collection = new CommandCollection { Commands = [Make.Command("a", ShellLanguage.Sh, "echo a")] };
        repo.Save(collection);
        repo.Save(collection); // unchanged – no new snapshot
        collection.Commands.Add(Make.Command("b", ShellLanguage.Sh, "echo b"));
        repo.Save(collection);

        var entries = repo.History.List();
        Assert.Equal(3, entries.Count);
        Assert.Equal(2, repo.History.Load(entries[0]).Commands.Count);
        Assert.Empty(repo.History.Load(entries[2]).Commands);
    }

    [Fact]
    public void Only_the_newest_snapshots_are_kept()
    {
        using var tmp = new TempDir();
        var history = new HistoryStore(tmp.Path);
        for (var i = 0; i < HistoryStore.MaxEntries + 5; i++)
            history.Record("{\"n\":" + i + "}", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i));

        var entries = history.List();
        Assert.Equal(HistoryStore.MaxEntries, entries.Count);
        Assert.Contains("\"n\":54", File.ReadAllText(entries[0].FilePath));
    }

    [Fact]
    public void Diff_detects_added_removed_changed_and_unchanged()
    {
        var a = Make.Command("a", ShellLanguage.Sh, "echo a");
        var b = Make.Command("b", ShellLanguage.Sh, "echo b");
        var c = Make.Command("c", ShellLanguage.Sh, "echo c");
        var before = new CommandCollection { Commands = [a, b] };
        var changedB = b.Clone();
        changedB.Script = "echo B";
        var after = new CommandCollection { Commands = [a.Clone(), changedB, c] };

        var diff = HistoryDiff.Compare(before, after).ToDictionary(d => d.Command.Name, d => d.Kind);

        Assert.Equal(ChangeKind.Unchanged, diff["a"]);
        Assert.Equal(ChangeKind.Changed, diff["b"]);
        Assert.Equal(ChangeKind.Added, diff["c"]);
        Assert.Equal(ChangeKind.Removed, HistoryDiff.Compare(after, before).Single(d => d.Command.Name == "c").Kind);
    }
}

public class ProfileImportTests
{
    [Fact]
    public void Sh_aliases_and_functions_are_found()
    {
        const string text = """
            # comment
            alias ll='ls -la' gs="git status"
            mkcd() { mkdir -p "$1" && cd "$1"; }
            function deploy {
              if [ -n "$1" ]; then
                echo "deploy {$1}"
              fi
            }
            # >>> QuickCommands >>>
            [ -f "$HOME/.quickcommands/qc.sh" ] && . "$HOME/.quickcommands/qc.sh"
            # <<< QuickCommands <<<
            prompt() { :; }
            """;

        var found = ProfileImporter.ScanSh("/home/x/.bashrc", text).ToDictionary(c => c.Name);

        Assert.Equal(["deploy", "gs", "ll", "mkcd"], found.Keys.Order());
        Assert.Equal("ls -la \"$@\"", found["ll"].Script);
        Assert.Equal("git status \"$@\"", found["gs"].Script);
        Assert.Equal("mkdir -p \"$1\" && cd \"$1\"", found["mkcd"].Script);
        Assert.Equal("if [ -n \"$1\" ]; then\n  echo \"deploy {$1}\"\nfi", found["deploy"].Script);
        Assert.Equal((4, 8), (found["deploy"].StartLine, found["deploy"].EndLine));
    }

    [Fact]
    public void Comment_out_prefixes_whole_lines_and_writes_a_backup()
    {
        using var tmp = new TempDir();
        var file = Path.Combine(tmp.Path, ".bashrc");
        File.WriteAllText(file, "export A=1\nmkcd() {\n  mkdir -p \"$1\"\n}\nalias ll='ls -la'\n");
        var candidates = ProfileImporter.ScanSh(file, File.ReadAllText(file));

        var backups = new ProfileImporter(tmp.CreatePaths()).CommentOut(candidates.Where(c => c.Name == "mkcd"));

        var p = ProfileImporter.CommentPrefix;
        Assert.Equal($"export A=1\n{p}mkcd() {{\n{p}  mkdir -p \"$1\"\n{p}}}\nalias ll='ls -la'\n", File.ReadAllText(file));
        Assert.Single(backups);
        Assert.StartsWith("export A=1\nmkcd()", File.ReadAllText(backups[0]));
    }

    [Fact]
    public async Task PowerShell_functions_and_aliases_are_found_via_the_parser()
    {
        Assert.SkipWhen(ExecutionTests.Pwsh is null, "pwsh not installed");
        using var worker = new PowerShellWorker();
        const string text = """
            Set-Alias -Name g -Value git
            function open { explorer . }
            function mkcd([string]$Name) {
                New-Item -ItemType Directory $Name | Out-Null
                Set-Location $Name
            }
            $x = 1; function inline { 'x' }
            function prompt { 'PS> ' }
            """;

        var defs = (await worker.FindDefinitionsAsync(text, TestContext.Current.CancellationToken))!.ToDictionary(d => d.Name);

        Assert.True(defs["g"].IsAlias);
        Assert.Equal("git @args", defs["g"].Script);
        Assert.Equal(" explorer . ", defs["open"].Script);
        Assert.StartsWith("param([string]$Name)\n", defs["mkcd"].Script.ReplaceLineEndings("\n"));
        Assert.Equal((3, 6), (defs["mkcd"].StartLine, defs["mkcd"].EndLine));
        Assert.True(defs.ContainsKey("inline"));
    }
}

public class SyntaxCheckerTests
{
    [Fact]
    public async Task PowerShell_errors_are_reported_with_line_numbers()
    {
        Assert.SkipWhen(ExecutionTests.Pwsh is null, "pwsh not installed");
        using var checker = new SyntaxChecker();
        var ct = TestContext.Current.CancellationToken;

        var ok = await checker.CheckAsync(ShellLanguage.PowerShell, "param($x)\nWrite-Output $x", ct);
        var bad = await checker.CheckAsync(ShellLanguage.PowerShell, "Write-Output 'a'\nif ($x {\n", ct);

        Assert.NotNull(ok);
        Assert.Empty(ok.Errors);
        Assert.True(ok.HasParamBlock);
        Assert.NotNull(bad);
        Assert.NotEmpty(bad.Errors);
        Assert.Equal(2, bad.Errors[0].Line);
    }

    [Fact]
    public async Task Sh_errors_are_reported_with_line_numbers()
    {
        Assert.SkipWhen(ExecutionTests.Bash is null, "bash / Git Bash not installed");
        using var checker = new SyntaxChecker();
        var ct = TestContext.Current.CancellationToken;

        var ok = await checker.CheckAsync(ShellLanguage.Sh, "echo ok\nif true; then echo ü; fi", ct);
        var bad = await checker.CheckAsync(ShellLanguage.Sh, "echo ok\n\nif then fi (", ct);

        Assert.NotNull(ok);
        Assert.Empty(ok.Errors);
        Assert.NotNull(bad);
        Assert.Equal(3, bad.Errors[0].Line);
    }

    [Fact]
    public async Task Cmd_is_not_checked()
    {
        using var checker = new SyntaxChecker();
        Assert.Null(await checker.CheckAsync(ShellLanguage.Cmd, "echo x", TestContext.Current.CancellationToken));
    }

    /// <summary>Every template must be valid – they are what new users start from.</summary>
    [Fact]
    public async Task All_templates_are_valid()
    {
        using var checker = new SyntaxChecker();
        var ct = TestContext.Current.CancellationToken;
        foreach (var t in TemplateLibrary.All)
        {
            Assert.True(CommandNameRules.IsValid(t.Name), t.Name);
            Assert.True(ParameterRules.Validate(t.Language, t.Parameters, t.Script).Count == 0, $"{t.Name} ({t.Language}) has parameter issues");
            foreach (var p in t.Parameters)
                Assert.True(t.Script.Contains(t.Language == ShellLanguage.Cmd ? "%" + p.Name + "%" : "$" + p.Name, StringComparison.OrdinalIgnoreCase) || t.Script.Contains("${" + p.Name, StringComparison.Ordinal), $"{t.Name} ({t.Language}) does not use {p.Name}");
            var result = await checker.CheckAsync(t.Language, t.Script, ct);
            if (result is not null)
                Assert.True(result.Errors.Count == 0, $"{t.Name} ({t.Language}): {string.Join("; ", result.Errors.Select(e => e.Message))}");
        }
        Assert.Equal(TemplateLibrary.All.Count, TemplateLibrary.All.Select(t => (t.Name, t.Language)).Distinct().Count());
    }
}

public class TrialRunnerTests
{
    private static QuickCommand Greet(ShellLanguage language, string script) => new()
    {
        Name = "greet",
        Language = language,
        Script = script,
        Parameters = [new() { Name = "who", Required = true }],
    };

    [Fact]
    public async Task PowerShell_trial_runs_the_draft_with_the_given_arguments()
    {
        Assert.SkipWhen(ExecutionTests.Pwsh is null, "pwsh not installed");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        var trial = new TrialRunner(paths).Prepare(Greet(ShellLanguage.PowerShell, "Write-Output \"Hello $who\""), "--who 'big world'", TerminalTexts.English);

        Assert.Equal(["-NoExit", "-ExecutionPolicy", "Bypass", "-File"], trial.Arguments.Take(4));
        var run = await ProcessRunner.RunAsync(trial.FileName, ["-NoProfile", "-File", trial.Arguments[^1]], TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        Assert.Contains("> greet --who 'big world'", run.StdOut);
        Assert.Contains("Hello big world", run.StdOut);
    }

    [Fact]
    public async Task Cmd_trial_runs_the_draft()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "cmd only exists on Windows");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        var trial = new TrialRunner(paths).Prepare(Greet(ShellLanguage.Cmd, "echo Hello %who%"), "\"big world\"", TerminalTexts.English);

        Assert.Equal("/k", trial.Arguments[0]);
        var output = await ExecutionTests.RunCmd(trial.Arguments[1]);
        Assert.Contains("Hello big world", output);
    }

    [Fact]
    public async Task Sh_trial_runs_the_draft_after_the_bashrc()
    {
        Assert.SkipWhen(ExecutionTests.Bash is null, "bash / Git Bash not installed");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        Directory.CreateDirectory(paths.Home);
        File.WriteAllText(Path.Combine(paths.Home, ".bashrc"), "echo from-bashrc\n");
        var trial = new TrialRunner(paths).Prepare(Greet(ShellLanguage.Sh, "echo \"Hello $who\""), "'big world'", TerminalTexts.English);

        var env = new Dictionary<string, string> { ["HOME"] = PathUtil.ToShPath(paths.Home) };
        var run = await ProcessRunner.RunAsync(ExecutionTests.Bash!, ["-c", ". " + ShGenerator.Quote(trial.Arguments[1])],
            TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken, env);
        Assert.Contains("from-bashrc\n\n> greet 'big world'\nHello big world", run.StdOut.ReplaceLineEndings("\n"));
    }
}

public class ProfileScanTests
{
    [Fact]
    public async Task Scan_finds_powershell_and_sh_profile_definitions()
    {
        Assert.SkipWhen(ExecutionTests.Pwsh is null, "pwsh not installed");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        var profile = paths.ProfilePath(ShellTarget.Pwsh);
        Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
        File.WriteAllText(profile, "Set-Alias -Name g -Value git\nfunction open { explorer . }\nfunction mkcd($Name) { New-Item -ItemType Directory $Name; Set-Location $Name }\nfunction prompt { \"PS $(Get-Location)> \" }\n");
        Directory.CreateDirectory(paths.Home);
        File.WriteAllText(Path.Combine(paths.Home, ".bashrc"), "alias ll='ls -la'\n");
        using var worker = new PowerShellWorker();

        var found = await new ProfileImporter(paths).ScanAsync(worker, TestContext.Current.CancellationToken);

        Assert.Equal(["g", "ll", "mkcd", "open"], found.Select(c => c.Name).Order());
    }
}

/// <summary>Opens a real terminal window – only runs when QC_LAUNCH_TEST=1 is set.</summary>
public class TrialLaunchTests
{
    [Fact]
    public async Task Launch_opens_a_terminal_that_runs_the_trial()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("QC_LAUNCH_TEST") == "1", "set QC_LAUNCH_TEST=1 to open a real terminal");
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var tmp = new TempDir();
        var paths = new QcPaths(Path.Combine(tmp.Path, "home with space"), Path.Combine(tmp.Path, "docs"), Path.Combine(tmp.Path, "config"));
        Directory.CreateDirectory(paths.Home);
        var marker = Path.Combine(tmp.Path, "marker.txt");
        var command = new QuickCommand
        {
            Name = "mark", Language = ShellLanguage.Cmd, Script = $"echo %who%> \"{marker}\"\r\nexit",
            Parameters = [new() { Name = "who", Required = true }],
        };

        var runner = new TrialRunner(paths);
        runner.Launch(runner.Prepare(command, "launched", TerminalTexts.English));

        for (var i = 0; i < 100 && !File.Exists(marker); i++)
            await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(marker), "the terminal did not run the trial");
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Equal("launched", File.ReadAllText(marker).Trim());
    }
}
