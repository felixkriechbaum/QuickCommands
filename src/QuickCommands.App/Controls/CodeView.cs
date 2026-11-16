using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using QuickCommands.Core;
using TextMateSharp.Grammars;

namespace QuickCommands.App.Controls;

/// <summary>Read-only script preview with the same syntax highlighting as the main editor, without wrapping.</summary>
public sealed class CodeView : UserControl
{
    public static readonly StyledProperty<string?> CodeProperty =
        AvaloniaProperty.Register<CodeView, string?>(nameof(Code));

    public static readonly StyledProperty<ShellLanguage> LanguageProperty =
        AvaloniaProperty.Register<CodeView, ShellLanguage>(nameof(Language));

    private readonly TextEditor _editor;
    private readonly RegistryOptions _registry;
    private readonly TextMate.Installation _textMate;

    public CodeView()
    {
        _editor = new TextEditor
        {
            IsReadOnly = true,
            ShowLineNumbers = false,
            WordWrap = false,
            Background = Brushes.Transparent,
            FontSize = 13,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        _editor.Bind(TextEditor.FontFamilyProperty, this.GetResourceObservable("QcMonoFont"));
        _editor.TextArea.Caret.CaretBrush = Brushes.Transparent;
        Content = _editor;

        _registry = new RegistryOptions(ThemeName.DarkPlus);
        _textMate = _editor.InstallTextMate(_registry);
        ApplyTheme();
        ApplyGrammar();
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
    }

    public string? Code
    {
        get => GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    public ShellLanguage Language
    {
        get => GetValue(LanguageProperty);
        set => SetValue(LanguageProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CodeProperty)
            _editor.Text = Code ?? "";
        else if (change.Property == LanguageProperty)
            ApplyGrammar();
    }

    private void ApplyTheme() =>
        _textMate.SetTheme(_registry.LoadTheme(ActualThemeVariant == ThemeVariant.Dark ? ThemeName.DarkPlus : ThemeName.LightPlus));

    private void ApplyGrammar() => _textMate.SetGrammar(_registry.GetScopeByLanguageId(Grammar(Language)));

    internal static string Grammar(ShellLanguage language) => language switch
    {
        ShellLanguage.PowerShell => "powershell",
        ShellLanguage.Cmd => "bat",
        _ => "shellscript",
    };
}
