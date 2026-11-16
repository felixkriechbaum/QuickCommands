using QuickCommands.App.Resources;
using QuickCommands.Core;

namespace QuickCommands.App.ViewModels;

/// <summary>Item for the language combo box. Shared instances, so selection matching works by reference.</summary>
public sealed class LanguageOption
{
    private LanguageOption(ShellLanguage value, string label, string badge)
    {
        Value = value;
        Label = label;
        Badge = badge;
    }

    public ShellLanguage Value { get; }
    public string Label { get; }
    public string Badge { get; }

    public static IReadOnlyList<LanguageOption> All { get; } =
    [
        new(ShellLanguage.PowerShell, Strings.Lang_PowerShell, "ps"),
        new(ShellLanguage.Cmd, Strings.Lang_Cmd, "cmd"),
        new(ShellLanguage.Sh, Strings.Lang_Sh, "sh"),
    ];

    public static LanguageOption For(ShellLanguage language) => All.First(o => o.Value == language);

    public override string ToString() => Label;
}

public sealed class KindOption
{
    private KindOption(ParameterKind value, string label)
    {
        Value = value;
        Label = label;
    }

    public ParameterKind Value { get; }
    public string Label { get; }

    public static IReadOnlyList<KindOption> All { get; } =
    [
        new(ParameterKind.Text, Strings.Kind_Text),
        new(ParameterKind.Switch, Strings.Kind_Switch),
        new(ParameterKind.Choice, Strings.Kind_Choice),
    ];

    public static KindOption For(ParameterKind kind) => All.First(o => o.Value == kind);

    public override string ToString() => Label;
}

public sealed class FilterOption
{
    private FilterOption(ShellLanguage? value, string label, string shortLabel)
    {
        Value = value;
        Label = label;
        ShortLabel = shortLabel;
    }

    public ShellLanguage? Value { get; }
    public string Label { get; }

    /// <summary>Label for the segmented filter above the list: "All", then the shell badges.</summary>
    public string ShortLabel { get; }

    public static IReadOnlyList<FilterOption> All { get; } =
    [
        new(null, Strings.Main_FilterAll, Strings.Main_FilterAllShort),
        .. LanguageOption.All.Select(o => new FilterOption(o.Value, o.Label, o.Badge)),
    ];

    public bool Matches(ShellLanguage language) => Value is null || Value == language;

    public override string ToString() => Label;
}

public sealed class UiLanguageOption
{
    private UiLanguageOption(string? code, string label)
    {
        Code = code;
        Label = label;
    }

    public string? Code { get; }
    public string Label { get; }

    public static IReadOnlyList<UiLanguageOption> All { get; } =
    [
        new(null, Strings.Settings_LanguageSystem),
        new("de", "Deutsch"),
        new("en", "English"),
    ];

    public override string ToString() => Label;
}

public static class ScriptTemplates
{
    public static string For(ShellLanguage language) => language switch
    {
        ShellLanguage.PowerShell => Strings.Template_PowerShell,
        ShellLanguage.Cmd => Strings.Template_Cmd,
        _ => Strings.Template_Sh,
    };
}

public static class ShellNames
{
    public static string For(ShellTarget target) => target switch
    {
        ShellTarget.Pwsh => Strings.Shell_Pwsh,
        ShellTarget.WindowsPowerShell => Strings.Shell_WindowsPowerShell,
        ShellTarget.Cmd => Strings.Shell_Cmd,
        ShellTarget.Bash => OperatingSystem.IsWindows() ? Strings.Shell_BashWindows : Strings.Shell_Bash,
        _ => Strings.Shell_Zsh,
    };
}
