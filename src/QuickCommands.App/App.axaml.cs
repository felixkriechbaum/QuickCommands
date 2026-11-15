using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using QuickCommands.App.ViewModels;
using QuickCommands.App.Views;

namespace QuickCommands.App;

public partial class App : Application
{
    public static AppServices Services { get; set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel(Services);
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
