using System.Globalization;
using Avalonia;
using QuickCommands.Core;

namespace QuickCommands.App;

internal static class Program
{
    /// <summary>Used by the uninstaller: removes the profile lines and the PATH entry, without any UI.</summary>
    public const string RemoveIntegrationArgument = "--remove-integration";

    [STAThread]
    public static int Main(string[] args)
    {
        using var services = AppServices.Create();
        if (args.Contains(RemoveIntegrationArgument, StringComparer.OrdinalIgnoreCase))
            return RemoveIntegration(services);

        ApplyUiLanguage(services.Settings.UiLanguage);
        App.Services = services;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static int RemoveIntegration(AppServices services)
    {
        var failed = 0;
        foreach (var target in ShellDetector.SupportedTargets)
        {
            try
            {
                if (services.Integration.IsInstalled(target))
                    services.Integration.Uninstall(target);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                failed++;
            }
        }
        return failed == 0 ? 0 : 1;
    }

    private static void ApplyUiLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return;
        var culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
