using QuickCommands.Core;

namespace QuickCommands.Tests;

public class GeneratorTests
{
    [Theory]
    [InlineData("abc", "'abc'")]
    [InlineData("it's", "'it''s'")]
    [InlineData("it’s", "'it’’s'")]
    public void PowerShell_quote_escapes_all_single_quote_variants(string input, string expected) =>
        Assert.Equal(expected, PowerShellGenerator.Quote(input));

    [Fact]
    public void Sh_quote_escapes_single_quotes() =>
        Assert.Equal("'it'\\''s'", ShGenerator.Quote("it's"));

    [Theory]
    [InlineData("a & b", "a ^& b")]
    [InlineData("a | b > c < d ^", "a ^| b ^> c ^< d ^^")]
    [InlineData("100%", "100%%")]
    [InlineData("say \"hi\"", "say 'hi'")]
    public void Cmd_echo_escaping(string input, string expected) =>
        Assert.Equal(expected, CmdGenerator.EscapeEcho(input));

    [Fact]
    public void Cmd_file_uses_crlf_carries_the_marker_and_has_no_setlocal()
    {
        var text = CmdGenerator.GenerateCommandFile(Make.Command("dev", ShellLanguage.Cmd, "cd /d D:\\Development\ncode ."));

        Assert.StartsWith("@echo off\r\n" + CmdGenerator.Marker + "\r\n", text);
        Assert.Contains("cd /d D:\\Development\r\ncode .\r\n", text);
        Assert.DoesNotContain("setlocal", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
    }

    [Fact]
    public void Sh_function_with_empty_body_gets_a_no_op()
    {
        var text = ShGenerator.Generate([Make.Command("noop", ShellLanguage.Sh, "  \n")], "\"$HOME/qc.sh\"");
        Assert.Contains("noop() {\n:\n}", text);
    }

    [Fact]
    public void Each_generator_only_defines_commands_of_its_own_language()
    {
        QuickCommand[] all =
        [
            Make.Command("pscmd", ShellLanguage.PowerShell, "Write-Output ps"),
            Make.Command("cmdcmd", ShellLanguage.Cmd, "echo cmd"),
            Make.Command("shcmd", ShellLanguage.Sh, "echo sh"),
        ];

        var ps = PowerShellGenerator.Generate(all, @"C:\x\qc.ps1");
        var sh = ShGenerator.Generate(all, "\"$HOME/qc.sh\"");

        Assert.Contains("${function:global:pscmd}", ps);
        Assert.DoesNotContain("${function:global:shcmd}", ps);
        Assert.Contains("shcmd() {", sh);
        Assert.DoesNotContain("pscmd() {", sh);
        // qc -a lists every language.
        Assert.Contains("cmdcmd", ps);
        Assert.Contains("cmdcmd", sh);
    }

    [Fact]
    public void Listing_puts_descriptions_on_one_line()
    {
        var lines = CommandListing.Lines([Make.Command("dev", ShellLanguage.Cmd, "", "line one\nline two")], "T", showLanguage: true, TerminalTexts.English);
        Assert.Contains(lines, l => l.Contains("dev") && l.Contains("cmd") && l.EndsWith("line one line two"));
    }
}
