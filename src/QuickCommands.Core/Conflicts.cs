using System.Text.RegularExpressions;

namespace QuickCommands.Core;

public static partial class CommandNameRules
{
    /// <summary>The terminal helper the tool generates itself.</summary>
    public const string ReservedName = "qc";
    public const int MaxLength = 64;

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*$")]
    private static partial Regex ValidName();

    public static string Normalize(string? name) => (name ?? "").Trim().ToLowerInvariant();

    public static bool IsValid(string normalizedName) =>
        normalizedName.Length is > 0 and <= MaxLength && ValidName().IsMatch(normalizedName);
}

public enum ConflictSeverity
{
    /// <summary>Saving requires the user to tick "deliberately override".</summary>
    Warning,

    /// <summary>Saving is impossible.</summary>
    Error,
}

public enum ConflictKind
{
    EmptyName,
    InvalidName,
    ReservedName,
    Duplicate,

    /// <summary>Internal cmd command (cd, mkdir, …) – always wins in cmd, so the command would never run.</summary>
    CmdBuiltin,

    /// <summary>A program in the system PATH comes before the user PATH and would always win in cmd.</summary>
    ShadowedBySystemPath,

    /// <summary>A program with this name exists in the PATH; the command takes precedence.</summary>
    ExistingExecutable,

    /// <summary>A language keyword (if, function, …) – cannot be a command name.</summary>
    ShellKeyword,

    /// <summary>A command the generated helper code itself depends on – overriding it would break QuickCommands.</summary>
    CriticalCommand,

    /// <summary>A shell builtin (sh) – the command takes precedence.</summary>
    ShellBuiltin,

    /// <summary>An existing PowerShell alias, function or cmdlet – the command takes precedence.</summary>
    ExistingPowerShellCommand,
}

/// <param name="Detail">Extra info for the message, e.g. the path of the shadowed program or the PowerShell command type.</param>
public sealed record Conflict(ConflictSeverity Severity, ConflictKind Kind, string? Detail = null);

public sealed record CommandKey(Guid Id, string Name, ShellLanguage Language);

public sealed class ConflictChecker(CommandInventory? inventory)
{
    /// <summary>True once the PATH / PowerShell inventory is available; before that only static rules are checked.</summary>
    public bool HasInventory => inventory is not null;

    public IReadOnlyList<Conflict> Check(string? rawName, ShellLanguage language, Guid selfId, IEnumerable<CommandKey> others)
    {
        var name = CommandNameRules.Normalize(rawName);
        var result = new List<Conflict>();

        if (name.Length == 0)
            return [new Conflict(ConflictSeverity.Error, ConflictKind.EmptyName)];
        if (!CommandNameRules.IsValid(name))
            return [new Conflict(ConflictSeverity.Error, ConflictKind.InvalidName)];
        if (name == CommandNameRules.ReservedName)
            return [new Conflict(ConflictSeverity.Error, ConflictKind.ReservedName)];

        if (others.Any(o => o.Id != selfId && o.Language == language && CommandNameRules.Normalize(o.Name) == name))
            result.Add(new Conflict(ConflictSeverity.Error, ConflictKind.Duplicate));

        var exe = inventory?.FindExecutable(name);
        switch (language)
        {
            case ShellLanguage.Cmd:
                if (ShellKnowledge.CmdBuiltins.Contains(name))
                    result.Add(new Conflict(ConflictSeverity.Error, ConflictKind.CmdBuiltin));
                else if (exe is { InSystemPath: true })
                    result.Add(new Conflict(ConflictSeverity.Error, ConflictKind.ShadowedBySystemPath, exe.Path));
                else if (exe is not null)
                    result.Add(new Conflict(ConflictSeverity.Warning, ConflictKind.ExistingExecutable, exe.Path));
                break;

            case ShellLanguage.PowerShell:
                if (ShellKnowledge.PowerShellKeywords.Contains(name))
                    result.Add(new Conflict(ConflictSeverity.Error, ConflictKind.ShellKeyword));
                else if (ShellKnowledge.PowerShellCritical.Contains(name))
                    result.Add(new Conflict(ConflictSeverity.Error, ConflictKind.CriticalCommand));
                else if (inventory?.FindPowerShellCommand(name) is { } type)
                    result.Add(new Conflict(ConflictSeverity.Warning, ConflictKind.ExistingPowerShellCommand, type));
                else if (exe is not null)
                    result.Add(new Conflict(ConflictSeverity.Warning, ConflictKind.ExistingExecutable, exe.Path));
                break;

            case ShellLanguage.Sh:
                if (ShellKnowledge.ShKeywords.Contains(name))
                    result.Add(new Conflict(ConflictSeverity.Error, ConflictKind.ShellKeyword));
                else if (ShellKnowledge.ShCritical.Contains(name))
                    result.Add(new Conflict(ConflictSeverity.Error, ConflictKind.CriticalCommand));
                else if (ShellKnowledge.ShBuiltins.Contains(name))
                    result.Add(new Conflict(ConflictSeverity.Warning, ConflictKind.ShellBuiltin));
                else if (exe is not null)
                    result.Add(new Conflict(ConflictSeverity.Warning, ConflictKind.ExistingExecutable, exe.Path));
                break;
        }

        return result;
    }
}

internal static class ShellKnowledge
{
    private static HashSet<string> Set(string words) =>
        new(words.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

    /// <summary>cmd.exe internal commands. They are resolved before anything on the PATH.</summary>
    public static readonly HashSet<string> CmdBuiltins = Set(
        "assoc break call cd chdir cls color copy date del dir dpath echo endlocal erase exit for ftype goto if " +
        "keys md mkdir mklink move path pause popd prompt pushd rd rem ren rename rmdir set setlocal shift start " +
        "time title type ver verify vol");

    public static readonly HashSet<string> PowerShellKeywords = Set(
        "begin break catch class clean continue data define do dynamicparam else elseif end enum exit filter finally " +
        "for foreach from function hidden if in inlinescript parallel param process return sequence static switch " +
        "throw trap try until using var while workflow");

    /// <summary>Cmdlets the generated qc.ps1 relies on.</summary>
    public static readonly HashSet<string> PowerShellCritical = Set(
        "remove-item test-path write-host write-output write-warning out-default out-host out-string format-table " +
        "foreach-object where-object select-object get-command set-item");

    public static readonly HashSet<string> ShKeywords = Set(
        "if then else elif fi case esac for select while until do done in function time coproc");

    /// <summary>Builtins the generated qc.sh relies on, or whose override would break the shell.</summary>
    public static readonly HashSet<string> ShCritical = Set(
        "builtin command return unset unalias printf source eval exec exit local set export");

    public static readonly HashSet<string> ShBuiltins = Set(
        "alias bg bind break caller cd compgen complete continue declare dirs disown echo enable false fc fg getopts " +
        "hash help history jobs kill let logout mapfile popd pushd pwd read readarray readonly shift shopt suspend " +
        "test times trap true type typeset ulimit umask wait");
}
