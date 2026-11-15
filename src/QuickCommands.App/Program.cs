using System.Globalization;
using Avalonia;
using QuickCommands.Core;

namespace QuickCommands.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var services = AppServices.Create();
        ApplyUiLanguage(services.Settings.UiLanguage);
        App.Services = services;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static void ApplyUiLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return;
        var culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
