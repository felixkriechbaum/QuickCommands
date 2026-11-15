using Avalonia.Controls;
using Avalonia.Interactivity;
using QuickCommands.App.Resources;
using QuickCommands.App.ViewModels;

namespace QuickCommands.App.Views;

/// <summary>Closes with true if commands were imported.</summary>
public partial class ImportWindow : Window
{
    public ImportWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => await ViewModel.LoadAsync();
    }

    private ImportViewModel ViewModel => (ImportViewModel)DataContext!;

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        ImportResult result;
        try
        {
            result = ViewModel.Import();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await new MessageDialog(Strings.Error_Title, ex.Message, Strings.Action_Ok, null).ShowDialog<bool>(this);
            return;
        }

        var lines = new List<string> { string.Format(Strings.Status_Imported, result.Count) };
        lines.AddRange(result.NotCommentedOut.Select(n => string.Format(Strings.Import_Inline, n)));
        if (result.Backups.Count > 0)
            lines.Add(string.Format(Strings.Import_Backups, string.Join(Environment.NewLine, result.Backups)));
        await new MessageDialog(Strings.Import_Title, string.Join(Environment.NewLine + Environment.NewLine, lines), Strings.Action_Ok, null)
            .ShowDialog<bool>(this);
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
