using System.Text.RegularExpressions;

namespace QuickCommands.Core;

public enum ParameterIssueKind
{
    EmptyName,
    InvalidName,
    DuplicateName,

    /// <summary>h / help – used for the generated help.</summary>
    ReservedName,

    /// <summary>The name is a special variable of the command's shell (PATH in cmd, $home in PowerShell, path in zsh, …).</summary>
    ShellVariable,

    NoChoices,
    InvalidChoice,
    DefaultNotInChoices,

    /// <summary>Default contains " or a line break – cannot be embedded safely in every shell.</summary>
    InvalidDefault,

    /// <summary>PowerShell script declares its own param() block although parameters are defined in the form.</summary>
    ScriptHasParamBlock,
}

/// <param name="Index">Index of the parameter, or -1 for issues of the whole command.</param>
public sealed record ParameterIssue(int Index, ParameterIssueKind Kind, string? Detail = null);

public static partial class ParameterRules
{
    public const int MaxNameLength = 32;

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex ValidName();

    [GeneratedRegex("^[A-Za-z0-9._+-]+$")]
    private static partial Regex ValidChoice();

    /// <summary>Optional #requires/comments/[CmdletBinding()] followed by param( at the start of the script.</summary>
    [GeneratedRegex(@"^(?:\s+|#[^\n]*|<#[\s\S]*?#>|\[CmdletBinding[^\]]*\])*param\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingParamBlock();

    public static string NormalizeName(string? name) => (name ?? "").Trim().ToLowerInvariant();

    /// <summary>Splits "dev|prod, test" into distinct trimmed values.</summary>
    public static List<string> ParseChoices(string? text) =>
        (text ?? "").Split(['|', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static bool HasLeadingParamBlock(string script) => LeadingParamBlock().IsMatch(script);

    public static IReadOnlyList<ParameterIssue> Validate(ShellLanguage language, IReadOnlyList<CommandParameter> parameters, string script)
    {
        var issues = new List<ParameterIssue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            var name = NormalizeName(p.Name);
            if (name.Length == 0)
                issues.Add(new(i, ParameterIssueKind.EmptyName));
            else if (name.Length > MaxNameLength || !ValidName().IsMatch(name))
                issues.Add(new(i, ParameterIssueKind.InvalidName, name));
            else if (name is "h" or "help")
                issues.Add(new(i, ParameterIssueKind.ReservedName, name));
            else if (ShellVariables(language).Contains(name))
                issues.Add(new(i, ParameterIssueKind.ShellVariable, name));
            else if (!seen.Add(name))
                issues.Add(new(i, ParameterIssueKind.DuplicateName, name));

            if (p.Kind == ParameterKind.Choice)
            {
                if (p.Choices.Count == 0)
                    issues.Add(new(i, ParameterIssueKind.NoChoices, name));
                foreach (var choice in p.Choices.Where(c => !ValidChoice().IsMatch(c)))
                    issues.Add(new(i, ParameterIssueKind.InvalidChoice, choice));
            }

            var effectiveDefault = p.EffectiveDefault;
            if (effectiveDefault.Length > 0)
            {
                if (effectiveDefault.Contains('"') || effectiveDefault.Contains('\n') || effectiveDefault.Contains('\r'))
                    issues.Add(new(i, ParameterIssueKind.InvalidDefault, name));
                else if (p.Kind == ParameterKind.Choice && p.Choices.Count > 0 && !p.Choices.Contains(effectiveDefault, StringComparer.Ordinal))
                    issues.Add(new(i, ParameterIssueKind.DefaultNotInChoices, effectiveDefault));
            }
        }

        if (parameters.Count > 0 && language == ShellLanguage.PowerShell && HasLeadingParamBlock(script))
            issues.Add(new(-1, ParameterIssueKind.ScriptHasParamBlock));
        return issues;
    }

    /// <summary>Variables a parameter must not shadow, because the generated variable would clash with them.</summary>
    public static IReadOnlySet<string> ShellVariables(ShellLanguage language) => language switch
    {
        ShellLanguage.PowerShell => PowerShellVariables,
        ShellLanguage.Cmd => CmdVariables,
        _ => ShVariables,
    };

    // Automatic and preference variables – many are read-only, the rest have a meaning PowerShell relies on.
    private static readonly HashSet<string> PowerShellVariables = new(StringComparer.Ordinal)
    {
        "args", "input", "this", "true", "false", "null", "home", "host", "pid", "pwd", "error", "matches",
        "myinvocation", "profile", "psitem", "shellid", "stacktrace", "event", "eventargs", "eventsubscriber",
        "sender", "foreach", "switch", "executioncontext", "lastexitcode", "psboundparameters", "pscmdlet",
        "pscommandpath", "psculture", "psdebugcontext", "pshome", "psscriptroot", "psuiculture", "psversiontable",
        "nestedpromptlevel", "ofs", "consolefilename", "islinux", "iswindows", "ismacos", "iscoreclr", "psedition",
        "erroractionpreference", "warningpreference", "verbosepreference", "debugpreference", "informationpreference",
        "progresspreference", "confirmpreference", "whatifpreference", "psitem", "psstyle",
    };

    // cmd variables are case-insensitive, so "path" would replace %PATH% inside the command.
    private static readonly HashSet<string> CmdVariables = new(StringComparer.Ordinal)
    {
        "path", "pathext", "comspec", "temp", "tmp", "systemroot", "windir", "userprofile", "homedrive", "homepath",
        "appdata", "localappdata", "programfiles", "programdata", "public", "systemdrive", "username", "userdomain",
        "computername", "os", "prompt", "cd", "date", "time", "random", "errorlevel", "cmdextversion", "cmdcmdline",
        "number_of_processors", "processor_architecture", "psmodulepath", "allusersprofile", "commonprogramfiles",
        "logonserver", "sessionname", "home",
    };

    // zsh ties these lowercase names to special arrays (path ↔ PATH); bash has no lowercase specials.
    private static readonly HashSet<string> ShVariables = new(StringComparer.Ordinal)
    {
        "path", "cdpath", "fpath", "manpath", "module_path", "mailpath", "psvar", "argv", "status", "pipestatus",
        "options", "functions", "aliases", "commands", "parameters", "history", "jobstates", "jobtexts", "jobdirs",
        "modules", "reswords", "userdirs", "nameddirs", "dirstack", "signals", "terminfo", "termcap", "widgets",
        "keymaps", "galiases", "saliases", "builtins", "words", "match", "mbegin", "mend", "reply", "prompt",
    };
}
