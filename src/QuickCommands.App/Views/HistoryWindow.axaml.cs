using Avalonia.Controls;
using Avalonia.Interactivity;
using QuickCommands.App.Resources;
using QuickCommands.App.ViewModels;

namespace QuickCommands.App.Views;

/// <summary>Closes with true if something was restored.</summary>
public partial class HistoryWindow : Window
{
    public HistoryWindow() => InitializeComponent();

    private HistoryViewModel ViewModel => (HistoryViewModel)DataContext!;

    private async void OnRestoreCommand(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel.RestoreCommand() is { } error)
            {
                await new MessageDialog(Strings.History_Title, error, Strings.Action_Ok, null).ShowDialog<bool>(this);
                return;
            }
            Close(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await new MessageDialog(Strings.Error_Title, ex.Message, Strings.Action_Ok, null).ShowDialog<bool>(this);
        }
    }

    private async void OnRestoreAll(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is not { } entry)
            return;
        var confirmed = await new MessageDialog(Strings.History_ConfirmTitle, string.Format(Strings.History_ConfirmAll, entry.Label),
            Strings.History_RestoreAll, Strings.Action_Cancel).ShowDialog<bool>(this);
        if (!confirmed)
            return;
        try
        {
            ViewModel.RestoreAll();
            Close(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await new MessageDialog(Strings.Error_Title, ex.Message, Strings.Action_Ok, null).ShowDialog<bool>(this);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(false);
}
