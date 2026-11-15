using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using QuickCommands.App.Resources;
using QuickCommands.App.ViewModels;

namespace QuickCommands.App.Views;

public partial class SetupWindow : Window
{
    public SetupWindow() => InitializeComponent();

    private ShellSetupViewModel ViewModel => (ShellSetupViewModel)DataContext!;

    private async void OnNext(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.Advance())
            return;

        var result = ViewModel.Apply();
        await MainWindow.ShowErrorsAsync(this, result);
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync(this, Strings.Setup_Data_Title);
        if (path is not null)
            ViewModel.DataDirectory = path;
    }

    internal static async Task<string?> PickFolderAsync(Window owner, string title)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
