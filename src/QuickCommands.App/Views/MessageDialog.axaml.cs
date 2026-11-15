using Avalonia.Controls;
using Avalonia.Interactivity;

namespace QuickCommands.App.Views;

/// <summary>Small confirm / info dialog. Without a cancel text it only shows one button.</summary>
public partial class MessageDialog : Window
{
    public MessageDialog() => InitializeComponent();

    public MessageDialog(string title, string message, string confirmText, string? cancelText) : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
        CancelButton.IsVisible = cancelText is not null;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
