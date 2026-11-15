using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using AvaloniaEdit.TextMate;
using QuickCommands.App.Resources;
using QuickCommands.App.ViewModels;
using QuickCommands.Core;
using TextMateSharp.Grammars;

namespace QuickCommands.App.Views;

public partial class MainWindow : Window, IDialogService
{
    private readonly RegistryOptions _registry;
    private readonly TextMate.Installation _textMate;
    private CommandItemViewModel? _editing;
    private bool _updatingEditor;
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();

        _registry = new RegistryOptions(IsDark ? ThemeName.DarkPlus : ThemeName.LightPlus);
        _textMate = Editor.InstallTextMate(_registry);
        Editor.TextChanged += OnEditorTextChanged;
        ActualThemeVariantChanged += (_, _) => _textMate.SetTheme(_registry.LoadTheme(IsDark ? ThemeName.DarkPlus : ThemeName.LightPlus));

        Opened += async (_, _) =>
        {
            if (ViewModel is { } vm)
                await vm.StartAsync();
        };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;
    private bool IsDark => ActualThemeVariant == ThemeVariant.Dark;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ViewModel is { } vm)
        {
            vm.Dialogs = this;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            ShowInEditor(vm.Selected);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Selected))
            ShowInEditor(ViewModel?.Selected);
    }

    /// <summary>The editor's text is not bindable, so it is synced by hand with the selected command.</summary>
    private void ShowInEditor(CommandItemViewModel? item)
    {
        if (_editing is not null)
            _editing.PropertyChanged -= OnEditingPropertyChanged;
        _editing = item;
        if (item is not null)
            item.PropertyChanged += OnEditingPropertyChanged;

        _updatingEditor = true;
        Editor.Text = item?.Script ?? "";
        _updatingEditor = false;
        Editor.IsEnabled = item is not null;
        ApplyGrammar(item?.Language.Value ?? ShellLanguage.PowerShell);

        if (item is { IsNew: true })
            NameBox.Focus();
    }

    private void OnEditingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CommandItemViewModel item)
            return;
        if (e.PropertyName == nameof(CommandItemViewModel.Language))
            ApplyGrammar(item.Language.Value);
        else if (e.PropertyName == nameof(CommandItemViewModel.Script) && Editor.Text != item.Script)
        {
            _updatingEditor = true;
            Editor.Text = item.Script;
            _updatingEditor = false;
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (!_updatingEditor && _editing is not null)
            _editing.Script = Editor.Text;
    }

    private void ApplyGrammar(ShellLanguage language)
    {
        var id = language switch
        {
            ShellLanguage.PowerShell => "powershell",
            ShellLanguage.Cmd => "bat",
            _ => "shellscript",
        };
        _textMate.SetGrammar(_registry.GetScopeByLanguageId(id));
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || ViewModel is not { HasUnsavedChanges: true })
            return;

        e.Cancel = true;
        if (await ConfirmAsync(Strings.Confirm_Close_Title, Strings.Confirm_Close_Message, Strings.Confirm_Close_Action))
        {
            _closeConfirmed = true;
            Close();
        }
    }

    // ---------- IDialogService ----------

    public Task<bool> ConfirmAsync(string title, string message, string confirmText) =>
        new MessageDialog(title, message, confirmText, Strings.Action_Cancel).ShowDialog<bool>(this);

    public async Task<bool> RunSetupAsync()
    {
        var vm = new ShellSetupViewModel(App.Services, isFirstRun: true);
        var completed = await new SetupWindow { DataContext = vm }.ShowDialog<bool>(this);
        if (!completed)
        {
            Close();
            return false;
        }
        return true;
    }

    public async Task<bool> ShowSettingsAsync()
    {
        var vm = new ShellSetupViewModel(App.Services, isFirstRun: false);
        return await new SettingsWindow { DataContext = vm }.ShowDialog<bool>(this);
    }

    internal static Task ShowErrorsAsync(Window owner, ApplyResult result)
    {
        if (result.Errors.Count == 0)
        {
            return result.Info is null
                ? Task.CompletedTask
                : new MessageDialog(Strings.Settings_Data, result.Info, Strings.Action_Ok, null).ShowDialog<bool>(owner);
        }
        var message = string.Format(Strings.Error_Apply, string.Join(Environment.NewLine, result.Errors));
        return new MessageDialog(Strings.Error_Title, message, Strings.Action_Ok, null).ShowDialog<bool>(owner);
    }
}
