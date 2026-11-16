using System.Globalization;
using Avalonia.Input;

namespace QuickCommands.App;

/// <summary>Keyboard shortcuts with the platform's command key (⌘ on macOS, Ctrl elsewhere) and their keycap labels.</summary>
public static class Shortcuts
{
    public static KeyModifiers CommandModifier { get; } = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    public static KeyGesture SaveGesture { get; } = new(Key.S, CommandModifier);
    public static KeyGesture NewGesture { get; } = new(Key.N, CommandModifier);
    public static KeyGesture DuplicateGesture { get; } = new(Key.D, CommandModifier);
    public static KeyGesture TryGesture { get; } = new(Key.F5);

    public static string Save => Label("S");
    public static string New => Label("N");
    public static string Duplicate => Label("D");
    public static string Try => "F5";

    // German keyboards label the key "Strg".
    private static string Label(string key) =>
        OperatingSystem.IsMacOS() ? "⌘" + key
        : (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "Strg+" : "Ctrl+") + key;
}
