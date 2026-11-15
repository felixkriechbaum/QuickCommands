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
var language = args.Length > 1 ? args[1] : "en";
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
DemoData.Write(services.Repository, paths, german: language == "de");
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
vm.Selected.Description = language == "de" ? "VS Code im aktuellen Ordner" : "VS Code in the current folder";
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
    public static void Write(CommandRepository repository, QcPaths paths, bool german)
    {
        string T(string de, string en) => german ? de : en;
        var dev = new QuickCommand
        {
            Name = "dev", Language = ShellLanguage.Cmd, Description = T("Zum Development-Ordner wechseln und VS Code öffnen", "Go to the development folder and open VS Code"),
            Script = "cd /d D:\\Development\\%project%\r\nif \"%code%\"==\"1\" code .",
            Parameters = [new() { Name = "project", Description = T("Unterordner", "Subfolder") }, new() { Name = "code", Kind = ParameterKind.Switch, Description = T("VS Code öffnen", "Open VS Code") }],
        };
        var mkcd = new QuickCommand
        {
            Name = "mkcd", Language = ShellLanguage.PowerShell, Description = T("Ordner anlegen und direkt hineinwechseln", "Create a folder and cd into it"),
            Script = T("# Ordner anlegen (auch verschachtelt) und hineinwechseln\n", "# Create the folder (nested is fine) and cd into it\n") + "New-Item -ItemType Directory -Force -Path $name | Out-Null\nSet-Location -Path $name\nWrite-Host \"" + T("Jetzt in", "Now in") + " $(Get-Location)\" -ForegroundColor Cyan\n",
            Parameters = [new() { Name = "name", Required = true, Description = T("Ordnername", "Folder name") }],
        };
        var deploy = new QuickCommand
        {
            Name = "deploy", Language = ShellLanguage.PowerShell, Description = T("App bauen und auf einen Server ausrollen", "Build the app and deploy it to a server"),
            Script = "Write-Host \"" + T("Baue $app für $target …", "Building $app for $target …") + "\" -ForegroundColor Cyan\ndotnet publish \"src/$app\" -c Release -o \"publish/$app\"\nif ($LASTEXITCODE -ne 0) { return }\n\nif (-not $force) {\n    if ((Read-Host \"" + T("Nach $target ausrollen? [j/N]\") -ne 'j'", "Deploy to ${target}? [y/N]\") -ne 'y'") + ") { return }\n}\nscp -r \"publish/$app\" \"deploy@$($target).example.com:/srv/$app\"\n",
            Parameters =
            [
                new() { Name = "app", Required = true, Description = T("Projektname", "Project name") },
                new() { Name = "target", Kind = ParameterKind.Choice, Choices = ["dev", "staging", "prod"], Default = "dev", Description = T("Zielumgebung", "Target environment") },
                new() { Name = "force", Kind = ParameterKind.Switch, Description = T("Ohne Rückfrage", "Don't ask for confirmation") },
            ],
        };
        var backup = new QuickCommand
        {
            Name = "backup", Language = ShellLanguage.PowerShell, Description = T("Projektordner als ZIP sichern", "Back up the project folder as a ZIP"),
            Script = "$stamp = Get-Date -Format 'yyyy-MM-dd'\nCompress-Archive -Path . -DestinationPath \"..\\backup-$stamp.zip\"\nif (Test-Path \"..\\backup-$stamp.zip\" {\n    Write-Host '" + T("Fertig", "Done") + "'\n}\n",
        };
        var collection = new CommandCollection { Commands = [dev, mkcd] };
        repository.Save(collection);
        collection.Commands.Add(new() { Name = "gs", Language = ShellLanguage.Sh, Description = T("Kurzer git status", "Short git status"), Script = "git status -sb" });
        collection.Commands.Add(new() { Name = "myip", Language = ShellLanguage.Cmd, Description = T("Lokale IPv4-Adressen anzeigen", "Show local IPv4 addresses"), Script = "ipconfig | findstr /i \"IPv4\"" });
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
