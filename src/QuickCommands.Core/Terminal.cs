using System.Globalization;

namespace QuickCommands.Core;

/// <summary>Texts printed by the generated scripts in the terminal (qc, --help, error messages).</summary>
public sealed record TerminalTexts(
    string Code,
    string NoCommands,
    string Footer,
    string TitleShell,
    string TitleAll,
    string ReloadedCount,
    string Reloaded,
    string CouldNotLoad,
    string CmdReload,
    string Usage,
    string Parameters,
    string Required,
    string DefaultValue,
    string Value,
    string MissingArgument,
    string MissingValue,
    string InvalidChoice,
    string UnknownCommand)
{
    public static TerminalTexts English { get; } = new(
        Code: "en",
        NoCommands: "(no quickcommands yet)",
        Footer: "qc -a: all shells   qc <name>: details   qc reload: reload commands",
        TitleShell: "QuickCommands ({0})",
        TitleAll: "QuickCommands (all shells)",
        ReloadedCount: "QuickCommands reloaded ({0} commands).",
        Reloaded: "QuickCommands reloaded.",
        CouldNotLoad: "QuickCommands: '{0}' could not be loaded",
        CmdReload: "cmd commands are available immediately - no reload needed.",
        Usage: "Usage:",
        Parameters: "Parameters:",
        Required: "required",
        DefaultValue: "default: {0}",
        Value: "value",
        MissingArgument: "{0}: missing argument <{1}>",
        MissingValue: "{0}: --{1} needs a value",
        InvalidChoice: "{0}: <{1}> must be one of: {2}",
        UnknownCommand: "qc: unknown command: ");

    public static TerminalTexts German { get; } = new(
        Code: "de",
        NoCommands: "(noch keine Quickcommands)",
        Footer: "qc -a: alle Shells   qc <name>: Details   qc reload: neu laden",
        TitleShell: "QuickCommands ({0})",
        TitleAll: "QuickCommands (alle Shells)",
        ReloadedCount: "QuickCommands neu geladen ({0} Commands).",
        Reloaded: "QuickCommands neu geladen.",
        CouldNotLoad: "QuickCommands: '{0}' konnte nicht geladen werden",
        CmdReload: "cmd-Commands sind sofort verfügbar - kein Neuladen nötig.",
        Usage: "Aufruf:",
        Parameters: "Parameter:",
        Required: "Pflicht",
        DefaultValue: "Standard: {0}",
        Value: "wert",
        MissingArgument: "{0}: Argument <{1}> fehlt",
        MissingValue: "{0}: --{1} braucht einen Wert",
        InvalidChoice: "{0}: <{1}> muss einer dieser Werte sein: {2}",
        UnknownCommand: "qc: unbekannter Command: ");

    /// <param name="uiLanguage">"de", "en" or null for the system language.</param>
    public static TerminalTexts For(string? uiLanguage)
    {
        var code = string.IsNullOrWhiteSpace(uiLanguage) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : uiLanguage;
        return code.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? German : English;
    }
}

/// <summary>Options shared by all generators.</summary>
public sealed record GeneratorOptions(TerminalTexts Texts, bool AutoReload)
{
    public static GeneratorOptions Default { get; } = new(TerminalTexts.English, AutoReload: true);
}

/// <summary>The help text of a command – printed by `name --help` and `qc name`.</summary>
public static class HelpText
{
    public static string LanguageLabel(ShellLanguage language) => language switch
    {
        ShellLanguage.PowerShell => "powershell",
        ShellLanguage.Cmd => "cmd",
        _ => "sh",
    };

    public static string UsageLine(QuickCommand command)
    {
        var parts = new List<string> { command.Name };
        foreach (var p in command.Parameters)
        {
            var name = ParameterRules.NormalizeName(p.Name);
            var token = p.Kind == ParameterKind.Switch ? "--" + name : "<" + name + ">";
            parts.Add(p.Required && p.Kind != ParameterKind.Switch ? token : "[" + token + "]");
        }
        return string.Join(' ', parts);
    }

    public static IReadOnlyList<string> Lines(QuickCommand command, TerminalTexts texts, bool includeLanguage)
    {
        var title = command.Name;
        if (includeLanguage)
            title += " (" + LanguageLabel(command.Language) + ")";
        var description = CommandListing.SingleLine(command.Description);
        if (description.Length > 0)
            title += " - " + description;

        var lines = new List<string> { title, "  " + texts.Usage + " " + UsageLine(command) };
        if (command.Parameters.Count == 0)
            return lines;

        var rows = command.Parameters.Select(p => (Left: Left(p, texts), Right: Right(p, texts))).ToList();
        var width = rows.Max(r => r.Left.Length);
        lines.Add("  " + texts.Parameters);
        foreach (var (left, right) in rows)
            lines.Add(("    " + left.PadRight(width) + "  " + right).TrimEnd());
        return lines;
    }

    private static string Left(CommandParameter p, TerminalTexts texts)
    {
        var name = ParameterRules.NormalizeName(p.Name);
        return p.Kind switch
        {
            ParameterKind.Switch => "--" + name,
            ParameterKind.Choice => "<" + name + ">, --" + name + " " + string.Join('|', p.Choices),
            _ => "<" + name + ">, --" + name + " <" + texts.Value + ">",
        };
    }

    private static string Right(CommandParameter p, TerminalTexts texts)
    {
        var parts = new List<string>();
        var description = CommandListing.SingleLine(p.Description);
        if (description.Length > 0)
            parts.Add(description);
        if (p.Required && p.Kind != ParameterKind.Switch)
            parts.Add("(" + texts.Required + ")");
        else if (p.EffectiveDefault.Length > 0)
            parts.Add("(" + string.Format(texts.DefaultValue, p.EffectiveDefault) + ")");
        return string.Join(' ', parts);
    }
}
