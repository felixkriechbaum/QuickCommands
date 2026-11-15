using System.Text;
using QuickCommands.Core;

namespace QuickCommands.Tests;

public class IntegrationTests
{
    [Fact]
    public void Installing_PowerShell_into_a_missing_profile_creates_it_with_bom()
    {
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        var service = new IntegrationService(paths, new FakePathEnvironment());

        service.Install(ShellTarget.Pwsh);

        var profile = paths.ProfilePath(ShellTarget.Pwsh);
        var bytes = File.ReadAllBytes(profile);
        Assert.True(bytes is [0xEF, 0xBB, 0xBF, ..]);
        Assert.Contains(paths.PowerShellScript, File.ReadAllText(profile));
        Assert.True(service.IsInstalled(ShellTarget.Pwsh));
        Assert.False(File.Exists(profile + IntegrationService.BackupSuffix));
    }

    [Fact]
    public void Installing_into_an_existing_profile_backs_it_up_once_and_uninstall_restores_it()
    {
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        var service = new IntegrationService(paths, new FakePathEnvironment());
        var profile = paths.ProfilePath(ShellTarget.Pwsh);
        const string original = "function open { explorer . }\r\nfnm use 22\r\n";
        Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
        File.WriteAllText(profile, original, new UTF8Encoding(false));

        service.Install(ShellTarget.Pwsh);
        File.AppendAllText(profile, "# user edit\r\n");
        service.Install(ShellTarget.Pwsh);

        Assert.Equal(original, File.ReadAllText(profile + IntegrationService.BackupSuffix));
        var installed = File.ReadAllText(profile);
        Assert.Single(installed.Split(ProfileBlock.Begin)[1..]);
        Assert.StartsWith(original, installed);

        service.Uninstall(ShellTarget.Pwsh);

        Assert.False(service.IsInstalled(ShellTarget.Pwsh));
        Assert.Equal(original + "# user edit\r\n", File.ReadAllText(profile));
        // No BOM was added to a file that had none.
        Assert.False(File.ReadAllBytes(profile) is [0xEF, 0xBB, 0xBF, ..]);
    }

    [Fact]
    public void Bash_profile_gets_lf_line_endings_and_a_home_relative_path()
    {
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        var service = new IntegrationService(paths, new FakePathEnvironment());

        service.Install(ShellTarget.Bash);

        var text = File.ReadAllText(paths.ProfilePath(ShellTarget.Bash));
        Assert.DoesNotContain("\r", text);
        Assert.Contains("[ -f \"$HOME/.quickcommands/qc.sh\" ] && . \"$HOME/.quickcommands/qc.sh\"", text);
    }

    [Fact]
    public void Cmd_integration_prepends_bin_to_the_user_path_and_removes_it_again()
    {
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        var env = new FakePathEnvironment();
        env.UserPath.Add(@"C:\tools");
        var service = new IntegrationService(paths, env);

        service.Install(ShellTarget.Cmd);
        service.Install(ShellTarget.Cmd);

        Assert.Equal([paths.BinDirectory, @"C:\tools"], env.UserPath);
        Assert.True(service.IsInstalled(ShellTarget.Cmd));

        service.Uninstall(ShellTarget.Cmd);

        Assert.Equal([@"C:\tools"], env.UserPath);
        Assert.False(service.IsInstalled(ShellTarget.Cmd));
    }

    [Fact]
    public void Repository_round_trips_commands()
    {
        using var tmp = new TempDir();
        var repo = new CommandRepository(Path.Combine(tmp.Path, "data"));
        var collection = new CommandCollection();
        collection.Commands.Add(Make.Command("dev", ShellLanguage.Cmd, "cd /d D:\\Development", "Grüße & so"));

        repo.Save(collection);
        var loaded = repo.Load();

        var c = Assert.Single(loaded.Commands);
        Assert.Equal("dev", c.Name);
        Assert.Equal(ShellLanguage.Cmd, c.Language);
        Assert.Equal("Grüße & so", c.Description);
        Assert.Equal(collection.Commands[0].Id, c.Id);
    }

    [Fact]
    public void Missing_repository_file_loads_as_empty()
    {
        using var tmp = new TempDir();
        Assert.Empty(new CommandRepository(tmp.Path).Load().Commands);
    }

    [Fact]
    public void Output_writer_removes_files_of_deleted_cmd_commands_but_keeps_foreign_files()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "cmd output is only written on Windows.");
        using var tmp = new TempDir();
        var paths = tmp.CreatePaths();
        var writer = new OutputWriter(paths);

        writer.WriteAll([Make.Command("one", ShellLanguage.Cmd, "echo 1"), Make.Command("two", ShellLanguage.Cmd, "echo 2")]);
        File.WriteAllText(Path.Combine(paths.BinDirectory, "mine.cmd"), "@echo off\r\necho not generated\r\n");
        writer.WriteAll([Make.Command("one", ShellLanguage.Cmd, "echo 1")]);

        Assert.True(File.Exists(Path.Combine(paths.BinDirectory, "one.cmd")));
        Assert.False(File.Exists(Path.Combine(paths.BinDirectory, "two.cmd")));
        Assert.True(File.Exists(Path.Combine(paths.BinDirectory, "mine.cmd")));
        Assert.True(File.Exists(Path.Combine(paths.BinDirectory, "qc.cmd")));
    }

    [Fact]
    public void Inventory_scan_keeps_the_first_match_and_skips_the_own_bin_folder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Uses PATHEXT based detection.");
        using var tmp = new TempDir();
        string Dir(string name)
        {
            var d = Path.Combine(tmp.Path, name);
            Directory.CreateDirectory(d);
            return d;
        }
        var system = Dir("system");
        var user = Dir("user");
        var own = Dir("own");
        File.WriteAllText(Path.Combine(system, "tool.exe"), "");
        File.WriteAllText(Path.Combine(user, "tool.cmd"), "");
        File.WriteAllText(Path.Combine(user, "other.bat"), "");
        File.WriteAllText(Path.Combine(user, "readme.txt"), "");
        File.WriteAllText(Path.Combine(own, "mine.cmd"), "");

        var result = InventoryLoader.ScanExecutables(
            [new PathDirectory(system, true), new PathDirectory(user, false), new PathDirectory(own, false)], own);

        Assert.True(result["TOOL"].InSystemPath);
        Assert.False(result["other"].InSystemPath);
        Assert.False(result.ContainsKey("readme"));
        Assert.False(result.ContainsKey("mine"));
    }
}
