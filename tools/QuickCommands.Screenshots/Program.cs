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
var language = args.Length > 1 ? args[1] : "de";
var culture = CultureInfo.GetCultureInfo(language);
CultureInfo.DefaultThreadCurrentUICulture = culture;
CultureInfo.CurrentUICulture = culture;
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.CurrentCulture = culture;
Directory.CreateDirectory(outputDir);

// Everything happens in a temp "home" – the real profile, PATH and commands.json stay untouched.
var temp = Path.Combine(Path.GetTempPath(), "qc-screenshots", Guid.NewGuid().ToString("N")[..8]);
var paths = new QcPaths(Path.Combine(temp, "home"), Path.Combine(temp, "docs"), Path.Combine(temp, "config"));
var settings = new AppSettings { SetupCompleted = true, EnabledShells = [ShellTarget.Pwsh, ShellTarget.Cmd], UiLanguage = language };
using var services = AppServices.Create(paths, new ReadOnlyPathEnvironment(PathEnvironment.CreateDefault()), settings);
DemoData.Write(services.Repository, paths);
App.Services = services;

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();
SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());

SetTheme(ThemeVariant.Dark);
var vm = new MainViewModel(services);
var main = new MainWindow { DataContext = vm, Width = 1180, Height = 780 };
main.Show();
WaitUntil(() => vm.Items.Count > 0 && !vm.IsCheckingConflicts, TimeSpan.FromSeconds(60));
Pump(TimeSpan.FromSeconds(2)); // initial syntax check of all commands

vm.Selected = vm.Items.First(i => i.Name == "deploy");
vm.Selected.TryArguments = "web prod --force";
Capture(main, "main-dark.png");

vm.Selected = vm.Items.First(i => i.Name == "backup");
WaitUntil(() => vm.Selected!.HasSyntaxErrors, TimeSpan.FromSeconds(20));
Capture(main, "syntax-dark.png");

vm.NewCommand.Execute(null);
vm.Selected!.Language = LanguageOption.For(ShellLanguage.Cmd);
vm.Selected.Name = "code";
vm.Selected.Description = "VS Code im aktuellen Ordner";
Capture(main, "conflict-dark.png");

SetTheme(ThemeVariant.Light);
vm.Selected = vm.Items.First(i => i.Name == "mkcd");
Capture(main, "main-light.png");
SetTheme(ThemeVariant.Dark);

var templates = new TemplatesWindow { DataContext = new TemplatesViewModel(services) };
templates.Show();
((TemplatesViewModel)templates.DataContext!).Selected = ((TemplatesViewModel)templates.DataContext!).Items.First(t => t.Name == "killport");
Capture(templates, "templates-dark.png");
templates.Close();

var importVm = new ImportViewModel(services, services.Repository.Load().Commands);
var import = new ImportWindow { DataContext = importVm };
import.Show();
WaitUntil(() => !importVm.IsScanning, TimeSpan.FromSeconds(30));
Capture(import, "import-dark.png");
import.Close();

var history = new HistoryWindow { DataContext = new HistoryViewModel(services, services.Repository.Load().Commands) };
history.Show();
var historyVm = (HistoryViewModel)history.DataContext!;
historyVm.Selected = historyVm.Entries.Skip(1).FirstOrDefault() ?? historyVm.Selected;
Capture(history, "history-dark.png");
history.Close();
main.Close();

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

services.Dispose();
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
    Pump(TimeSpan.FromMilliseconds(500));
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
    /// <summary>Saves the demo commands in a few steps (so the history has entries) and writes demo profiles for the import.</summary>
    public static void Write(CommandRepository repository, QcPaths paths)
    {
        var dev = new QuickCommand
        {
            Name = "dev", Language = ShellLanguage.Cmd, Description = "Zum Development-Ordner wechseln und VS Code öffnen",
            Script = "cd /d D:\\Development\\%project%\r\nif \"%code%\"==\"1\" code .",
            Parameters = [new() { Name = "project", Description = "Unterordner" }, new() { Name = "code", Kind = ParameterKind.Switch, Description = "VS Code öffnen" }],
        };
        var mkcd = new QuickCommand
        {
            Name = "mkcd", Language = ShellLanguage.PowerShell, Description = "Ordner anlegen und direkt hineinwechseln",
            Script = "# Ordner anlegen (auch verschachtelt) und hineinwechseln\nNew-Item -ItemType Directory -Force -Path $name | Out-Null\nSet-Location -Path $name\nWrite-Host \"Jetzt in $(Get-Location)\" -ForegroundColor Cyan\n",
            Parameters = [new() { Name = "name", Required = true, Description = "Ordnername" }],
        };
        var deploy = new QuickCommand
        {
            Name = "deploy", Language = ShellLanguage.PowerShell, Description = "App bauen und auf einen Server ausrollen",
            Script = "Write-Host \"Baue $app für $target …\" -ForegroundColor Cyan\ndotnet publish \"src/$app\" -c Release -o \"publish/$app\"\nif ($LASTEXITCODE -ne 0) { return }\n\nif (-not $force) {\n    if ((Read-Host \"Nach $target ausrollen? [j/N]\") -ne 'j') { return }\n}\nscp -r \"publish/$app\" \"deploy@$($target).example.com:/srv/$app\"\n",
            Parameters =
            [
                new() { Name = "app", Required = true, Description = "Projektname" },
                new() { Name = "target", Kind = ParameterKind.Choice, Choices = ["dev", "staging", "prod"], Default = "dev", Description = "Zielumgebung" },
                new() { Name = "force", Kind = ParameterKind.Switch, Description = "Ohne Rückfrage" },
            ],
        };
        var backup = new QuickCommand
        {
            Name = "backup", Language = ShellLanguage.PowerShell, Description = "Projektordner als ZIP sichern",
            Script = "$stamp = Get-Date -Format 'yyyy-MM-dd'\nCompress-Archive -Path . -DestinationPath \"..\\backup-$stamp.zip\"\nif (Test-Path \"..\\backup-$stamp.zip\" {\n    Write-Host 'Fertig'\n}\n",
        };
        var collection = new CommandCollection { Commands = [dev, mkcd] };
        repository.Save(collection);
        collection.Commands.Add(new() { Name = "gs", Language = ShellLanguage.Sh, Description = "Kurzer git status", Script = "git status -sb" });
        collection.Commands.Add(new() { Name = "myip", Language = ShellLanguage.Cmd, Description = "Lokale IPv4-Adressen anzeigen", Script = "ipconfig | findstr /i \"IPv4\"" });
        repository.Save(collection);
        collection.Commands.Add(deploy);
        collection.Commands.Add(backup);
        collection.Commands.RemoveAll(c => c.Name == "myip");
        repository.Save(collection);

        var profile = paths.ProfilePath(ShellTarget.Pwsh);
        Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
        File.WriteAllText(profile, "Set-Alias -Name g -Value git\nfunction open { explorer . }\nfunction mkcd($Name) { New-Item -ItemType Directory $Name; Set-Location $Name }\nfunction prompt { \"PS $(Get-Location)> \" }\n");
        Directory.CreateDirectory(paths.Home);
        File.WriteAllText(Path.Combine(paths.Home, ".bashrc"), "alias ll='ls -la'\nalias gl='git log --oneline -20'\nextract() {\n  tar -xf \"$1\"\n}\n");
    }
}
