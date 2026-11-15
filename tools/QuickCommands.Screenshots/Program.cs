using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using QuickCommands.App;
using QuickCommands.App.ViewModels;
using QuickCommands.App.Views;
using QuickCommands.Core;

// Usage: dotnet run --project tools/QuickCommands.Screenshots -- [outputDir] [de|en]
var outputDir = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("docs", "screenshots"));
var culture = CultureInfo.GetCultureInfo(args.Length > 1 ? args[1] : "de");
CultureInfo.DefaultThreadCurrentUICulture = culture;
CultureInfo.CurrentUICulture = culture;
Directory.CreateDirectory(outputDir);

// Everything happens in a temp "home" – the real profile, PATH and commands.json stay untouched.
var temp = Path.Combine(Path.GetTempPath(), "qc-screenshots", Guid.NewGuid().ToString("N")[..8]);
var paths = new QcPaths(Path.Combine(temp, "home"), Path.Combine(temp, "docs"), Path.Combine(temp, "config"));
var settings = new AppSettings { SetupCompleted = true, EnabledShells = [ShellTarget.Pwsh, ShellTarget.Cmd] };
var services = AppServices.Create(paths, new ReadOnlyPathEnvironment(PathEnvironment.CreateDefault()), settings);
new CommandRepository(services.DataDirectory).Save(DemoData.Commands());
App.Services = services;

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();
SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());

SetTheme(ThemeVariant.Dark);
var vm = new MainViewModel(services);
var main = new MainWindow { DataContext = vm, Width = 1120, Height = 720 };
main.Show();
WaitUntil(() => vm.Items.Count > 0 && !vm.IsCheckingConflicts, TimeSpan.FromSeconds(60));

vm.Selected = vm.Items.First(i => i.Name == "mkcd");
Capture(main, "main-dark.png");

vm.NewCommand.Execute(null);
vm.Selected!.Language = LanguageOption.For(ShellLanguage.Cmd);
vm.Selected.Name = "code";
vm.Selected.Description = "VS Code im aktuellen Ordner";
Capture(main, "conflict-dark.png");

SetTheme(ThemeVariant.Light);
vm.Selected = vm.Items.First(i => i.Name == "dev");
Capture(main, "main-light.png");
main.Close();

SetTheme(ThemeVariant.Dark);
var setupVm = new ShellSetupViewModel(services, isFirstRun: true);
var setup = new SetupWindow { DataContext = setupVm };
setup.Show();
Capture(setup, "setup-step1-dark.png");
setupVm.Step = 3;
Capture(setup, "setup-step3-dark.png");
setup.Close();

var settingsWindow = new SettingsWindow { DataContext = new ShellSetupViewModel(services, isFirstRun: false) };
settingsWindow.Show();
Capture(settingsWindow, "settings-dark.png");
settingsWindow.Close();

try
{
    Directory.Delete(temp, recursive: true);
}
catch (IOException)
{
}
Console.WriteLine($"Screenshots written to {outputDir}");
return;

void SetTheme(ThemeVariant variant)
{
    Application.Current!.RequestedThemeVariant = variant;
    Pump(TimeSpan.FromMilliseconds(100));
}

void Capture(Window window, string fileName)
{
    Pump(TimeSpan.FromMilliseconds(400));
    var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered.");
    var file = Path.Combine(outputDir, fileName);
    using (var stream = File.Create(file))
    {
#pragma warning disable CS0618 // The replacement overload's options type differs between Avalonia 12 minors; PNG defaults are fine.
        frame.Save(stream);
#pragma warning restore CS0618
    }
    Console.WriteLine(file);
}

static void Pump(TimeSpan duration)
{
    var sw = Stopwatch.StartNew();
    while (sw.Elapsed < duration)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(10);
    }
}

static void WaitUntil(Func<bool> condition, TimeSpan timeout)
{
    var sw = Stopwatch.StartNew();
    while (!condition() && sw.Elapsed < timeout)
        Pump(TimeSpan.FromMilliseconds(50));
}

internal sealed class ReadOnlyPathEnvironment(IPathEnvironment inner) : IPathEnvironment
{
    public bool SupportsUserPathEditing => inner.SupportsUserPathEditing;
    public IReadOnlyList<PathDirectory> GetSearchDirectories() => inner.GetSearchDirectories();
    public bool UserPathContains(string directory) => inner.UserPathContains(directory);
    public void AddToUserPath(string directory) => throw new InvalidOperationException("Screenshots never change the PATH.");
    public void RemoveFromUserPath(string directory) => throw new InvalidOperationException("Screenshots never change the PATH.");
}

internal static class DemoData
{
    public static CommandCollection Commands() => new()
    {
        Commands =
        [
            new() { Name = "dev", Language = ShellLanguage.Cmd, Description = "Zum Development-Ordner wechseln und VS Code öffnen",
                    Script = "cd /d D:\\Development\r\ncode ." },
            new() { Name = "mkcd", Language = ShellLanguage.PowerShell, Description = "Ordner anlegen und direkt hineinwechseln",
                    Script = "param([Parameter(Mandatory)][string]$Name)\n\n# Ordner anlegen (auch verschachtelt) und hineinwechseln\nNew-Item -ItemType Directory -Force -Path $Name | Out-Null\nSet-Location -Path $Name\nWrite-Host \"Jetzt in $(Get-Location)\" -ForegroundColor Cyan\n" },
            new() { Name = "open", Language = ShellLanguage.PowerShell, Description = "Explorer im aktuellen Ordner öffnen",
                    Script = "explorer ." },
            new() { Name = "serve", Language = ShellLanguage.PowerShell, Description = "Statischen Webserver auf Port 5173 starten",
                    Script = "npx serve . -l 5173" },
            new() { Name = "gs", Language = ShellLanguage.Sh, Description = "Kurzer git status",
                    Script = "git status -sb" },
            new() { Name = "myip", Language = ShellLanguage.Cmd, Description = "Lokale IPv4-Adressen anzeigen",
                    Script = "ipconfig | findstr /i \"IPv4\"" },
        ],
    };
}
