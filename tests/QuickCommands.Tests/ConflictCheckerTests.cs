using QuickCommands.Core;

namespace QuickCommands.Tests;

public class ConflictCheckerTests
{
    private static readonly CommandInventory Inventory = new(
        new Dictionary<string, ExecutableInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["where"] = new(@"C:\Windows\System32\where.exe", InSystemPath: true),
            ["code"] = new(@"C:\Users\x\AppData\Local\Programs\Microsoft VS Code\bin\code.cmd", InSystemPath: false),
        },
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ls"] = "Alias",
            ["mkdir"] = "Function",
            ["get-childitem"] = "Cmdlet",
        });

    private static IReadOnlyList<Conflict> Check(string name, ShellLanguage language, CommandInventory? inventory = null, params CommandKey[] others) =>
        new ConflictChecker(inventory ?? Inventory).Check(name, language, Guid.NewGuid(), others);

    private static void AssertSingle(IReadOnlyList<Conflict> conflicts, ConflictSeverity severity, ConflictKind kind)
    {
        var conflict = Assert.Single(conflicts);
        Assert.Equal(severity, conflict.Severity);
        Assert.Equal(kind, conflict.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_name_is_an_error(string name) =>
        AssertSingle(Check(name, ShellLanguage.Cmd), ConflictSeverity.Error, ConflictKind.EmptyName);

    [Theory]
    [InlineData("make dir")]
    [InlineData("-x")]
    [InlineData("grüß")]
    [InlineData("a.b")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Invalid_names_are_errors(string name) =>
        AssertSingle(Check(name, ShellLanguage.PowerShell), ConflictSeverity.Error, ConflictKind.InvalidName);

    [Theory]
    [InlineData("dev")]
    [InlineData("Dev")]
    [InlineData("mk-cd_2")]
    public void Valid_free_names_have_no_conflicts(string name) =>
        Assert.Empty(Check(name, ShellLanguage.PowerShell));

    [Fact]
    public void Qc_is_reserved() =>
        AssertSingle(Check("QC", ShellLanguage.Sh), ConflictSeverity.Error, ConflictKind.ReservedName);

    [Fact]
    public void Same_name_in_the_same_language_is_a_duplicate()
    {
        var other = new CommandKey(Guid.NewGuid(), "dev", ShellLanguage.Cmd);
        AssertSingle(Check("DEV", ShellLanguage.Cmd, null, other), ConflictSeverity.Error, ConflictKind.Duplicate);
    }

    [Fact]
    public void Same_name_in_another_language_is_allowed()
    {
        var other = new CommandKey(Guid.NewGuid(), "dev", ShellLanguage.Cmd);
        Assert.Empty(Check("dev", ShellLanguage.PowerShell, null, other));
    }

    [Fact]
    public void A_command_does_not_conflict_with_itself()
    {
        var id = Guid.NewGuid();
        var conflicts = new ConflictChecker(Inventory).Check("dev", ShellLanguage.Cmd, id, [new CommandKey(id, "dev", ShellLanguage.Cmd)]);
        Assert.Empty(conflicts);
    }

    [Theory]
    [InlineData("mkdir")]
    [InlineData("cd")]
    [InlineData("DIR")]
    public void Cmd_builtins_are_blocked(string name) =>
        AssertSingle(Check(name, ShellLanguage.Cmd), ConflictSeverity.Error, ConflictKind.CmdBuiltin);

    [Fact]
    public void Cmd_program_in_system_path_is_blocked_because_it_would_always_win()
    {
        var conflicts = Check("where", ShellLanguage.Cmd);
        AssertSingle(conflicts, ConflictSeverity.Error, ConflictKind.ShadowedBySystemPath);
        Assert.EndsWith("where.exe", conflicts[0].Detail);
    }

    [Fact]
    public void Cmd_program_in_user_path_is_only_a_warning() =>
        AssertSingle(Check("code", ShellLanguage.Cmd), ConflictSeverity.Warning, ConflictKind.ExistingExecutable);

    [Fact]
    public void PowerShell_keyword_is_blocked() =>
        AssertSingle(Check("foreach", ShellLanguage.PowerShell), ConflictSeverity.Error, ConflictKind.ShellKeyword);

    [Fact]
    public void PowerShell_command_used_by_the_generated_code_is_blocked() =>
        AssertSingle(Check("Remove-Item", ShellLanguage.PowerShell), ConflictSeverity.Error, ConflictKind.CriticalCommand);

    [Theory]
    [InlineData("ls", "Alias")]
    [InlineData("mkdir", "Function")]
    [InlineData("get-childitem", "Cmdlet")]
    public void Existing_PowerShell_command_is_a_warning(string name, string type)
    {
        var conflicts = Check(name, ShellLanguage.PowerShell);
        AssertSingle(conflicts, ConflictSeverity.Warning, ConflictKind.ExistingPowerShellCommand);
        Assert.Equal(type, conflicts[0].Detail);
    }

    [Fact]
    public void PowerShell_program_on_path_is_a_warning() =>
        AssertSingle(Check("where", ShellLanguage.PowerShell), ConflictSeverity.Warning, ConflictKind.ExistingExecutable);

    [Fact]
    public void Sh_keyword_is_blocked() =>
        AssertSingle(Check("fi", ShellLanguage.Sh), ConflictSeverity.Error, ConflictKind.ShellKeyword);

    [Fact]
    public void Sh_builtin_used_by_the_generated_code_is_blocked() =>
        AssertSingle(Check("unset", ShellLanguage.Sh), ConflictSeverity.Error, ConflictKind.CriticalCommand);

    [Fact]
    public void Sh_builtin_is_a_warning() =>
        AssertSingle(Check("cd", ShellLanguage.Sh), ConflictSeverity.Warning, ConflictKind.ShellBuiltin);

    [Fact]
    public void Without_inventory_only_static_rules_apply()
    {
        var checker = new ConflictChecker(null);
        Assert.False(checker.HasInventory);
        Assert.Empty(checker.Check("code", ShellLanguage.Cmd, Guid.NewGuid(), []));
        Assert.NotEmpty(checker.Check("mkdir", ShellLanguage.Cmd, Guid.NewGuid(), []));
    }
}
