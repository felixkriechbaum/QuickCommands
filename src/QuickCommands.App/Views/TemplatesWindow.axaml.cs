using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using QuickCommands.App.ViewModels;

namespace QuickCommands.App.Views;

/// <summary>Returns the chosen template as a new command draft, or null.</summary>
public partial class TemplatesWindow : Window
{
    public TemplatesWindow() => InitializeComponent();

    private TemplatesViewModel ViewModel => (TemplatesViewModel)DataContext!;

    private void OnUse(object? sender, RoutedEventArgs e) => Close(ViewModel.Selected?.Command.Clone());

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel.Selected is not null)
            Close(ViewModel.Selected.Command.Clone());
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
