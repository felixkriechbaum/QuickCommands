using Avalonia.Controls;
using Avalonia.Interactivity;
using QuickCommands.App.Resources;
using QuickCommands.App.ViewModels;

namespace QuickCommands.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    private ShellSetupViewModel ViewModel => (ShellSetupViewModel)DataContext!;

    private async void OnApply(object? sender, RoutedEventArgs e)
    {
        var result = ViewModel.Apply();
        await MainWindow.ShowErrorsAsync(this, result);
        Close(result.DataDirectoryChanged);
    }

    private async void OnRemoveAll(object? sender, RoutedEventArgs e)
    {
        var confirmed = await new MessageDialog(Strings.Settings_RemoveAll_Title, Strings.Settings_RemoveAll_Message,
            Strings.Settings_RemoveAll, Strings.Action_Cancel).ShowDialog<bool>(this);
        if (!confirmed)
            return;
        ViewModel.UncheckAll();
        OnApply(sender, e);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var path = await SetupWindow.PickFolderAsync(this, Strings.Settings_Data);
        if (path is not null)
            ViewModel.DataDirectory = path;
    }
}
