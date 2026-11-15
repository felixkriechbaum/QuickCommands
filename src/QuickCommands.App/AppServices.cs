using QuickCommands.Core;

namespace QuickCommands.App;

/// <summary>Everything the view models need, wired up once at startup.</summary>
public sealed class AppServices
{
    private Task<CommandInventory>? _inventory;

    private AppServices(QcPaths paths, IPathEnvironment pathEnvironment, AppSettings settings)
    {
        Paths = paths;
        PathEnvironment = pathEnvironment;
        Settings = settings;
        Integration = new IntegrationService(paths, pathEnvironment);
        Output = new OutputWriter(paths);
    }

    public static AppServices Create()
    {
        var paths = QcPaths.ForCurrentUser();
        return new AppServices(paths, Core.PathEnvironment.CreateDefault(), SettingsStore.Load(paths.SettingsFile));
    }

    /// <summary>For tools and tests that must not touch the real profile / PATH.</summary>
    public static AppServices Create(QcPaths paths, IPathEnvironment pathEnvironment, AppSettings settings) =>
        new(paths, pathEnvironment, settings);

    public QcPaths Paths { get; }
    public IPathEnvironment PathEnvironment { get; }
    public AppSettings Settings { get; }
    public IntegrationService Integration { get; }
    public OutputWriter Output { get; }

    public string DataDirectory => Paths.ResolveDataDirectory(Settings);
    public CommandRepository Repository => new(DataDirectory);

    /// <summary>Scanning PATH and asking PowerShell takes a few seconds, so it runs once in the background.</summary>
    public Task<CommandInventory> Inventory =>
        _inventory ??= Task.Run(() => InventoryLoader.LoadAsync(PathEnvironment, Paths.BinDirectory));

    public void SaveSettings() => SettingsStore.Save(Paths.SettingsFile, Settings);

    public bool IsLanguageSetUp(ShellLanguage language) =>
        Settings.EnabledShells.Any(t => t.Language() == language);
}
