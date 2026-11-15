using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickCommands.App.Resources;
using QuickCommands.Core;

namespace QuickCommands.App.ViewModels;

public sealed partial class ShellOptionViewModel : ObservableObject
{
    public ShellOptionViewModel(ShellTarget target, bool isAvailable, bool isInstalled, bool isChecked)
    {
        Target = target;
        IsAvailable = isAvailable;
        IsInstalled = isInstalled;
        IsChecked = isChecked;
    }

    public ShellTarget Target { get; }
    public string Label => ShellNames.For(Target);
    public bool IsAvailable { get; }
    public bool IsInstalled { get; }

    /// <summary>A missing shell can still be unchecked if it was set up earlier.</summary>
    public bool IsEnabled => IsAvailable || IsInstalled;

    public string? StatusText => IsInstalled ? Strings.Shell_Installed : IsAvailable ? null : Strings.Shell_NotFound;
    public bool HasStatus => StatusText is not null;

    [ObservableProperty]
    public partial bool IsChecked { get; set; }
}

public sealed record ApplyResult(bool DataDirectoryChanged, IReadOnlyList<string> Errors, string? Info);

/// <summary>Shared by the first-run wizard (3 steps) and the settings window.</summary>
public sealed partial class ShellSetupViewModel : ObservableObject
{
    private readonly AppServices _services;

    public ShellSetupViewModel(AppServices services, bool isFirstRun)
    {
        _services = services;
        IsFirstRun = isFirstRun;
        DataDirectory = services.DataDirectory;
        SelectedUiLanguage = UiLanguageOption.All.FirstOrDefault(o => o.Code == services.Settings.UiLanguage) ?? UiLanguageOption.All[0];
        AutoReload = services.Settings.AutoReload;
        Step = 1;

        foreach (var target in ShellDetector.SupportedTargets)
        {
            var installed = services.Integration.IsInstalled(target);
            var available = ShellDetector.IsAvailable(target);
            var isChecked = isFirstRun ? installed || (available && ShellDetector.IsDefaultChoice(target)) : installed;
            var option = new ShellOptionViewModel(target, available, installed, isChecked);
            option.PropertyChanged += (_, _) => OnPropertyChanged(nameof(PlannedChanges));
            Shells.Add(option);
        }
    }

    public bool IsFirstRun { get; }
    public ObservableCollection<ShellOptionViewModel> Shells { get; } = [];
    public IReadOnlyList<UiLanguageOption> UiLanguages => UiLanguageOption.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlannedChanges))]
    public partial string DataDirectory { get; set; }

    [ObservableProperty]
    public partial UiLanguageOption SelectedUiLanguage { get; set; }

    [ObservableProperty]
    public partial bool AutoReload { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(StepText), nameof(NextLabel), nameof(CanGoBack), nameof(PlannedChanges))]
    public partial int Step { get; set; }

    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;
    public bool CanGoBack => Step > 1;
    public string StepText => string.Format(Strings.Setup_Step, Step);
    public string NextLabel => Step == 3 ? Strings.Action_Install : Strings.Action_Next;

    /// <summary>Human-readable list of what Apply will change – shown before anything is touched.</summary>
    public IReadOnlyList<string> PlannedChanges
    {
        get
        {
            var lines = new List<string>();
            foreach (var shell in Shells)
            {
                if (shell.IsChecked && !shell.IsInstalled)
                {
                    lines.Add(shell.Target == ShellTarget.Cmd
                        ? string.Format(Strings.Setup_Summary_Path, shell.Label, _services.Paths.BinDirectory)
                        : string.Format(Strings.Setup_Summary_Profile, shell.Label, _services.Paths.ProfilePath(shell.Target)));
                }
                else if (!shell.IsChecked && shell.IsInstalled)
                {
                    lines.Add(string.Format(Strings.Setup_Summary_Remove, shell.Label));
                }
            }
            if (!Shells.Any(s => s.IsChecked))
                lines.Add(Strings.Setup_Summary_None);
            lines.Add(string.Format(Strings.Setup_Summary_Data, DataDirectory));
            return lines;
        }
    }

    [RelayCommand]
    private void Back() => Step = Math.Max(1, Step - 1);

    /// <summary>Returns true when the wizard is on its last step and the caller should apply.</summary>
    public bool Advance()
    {
        if (Step < 3)
        {
            Step++;
            return false;
        }
        return true;
    }

    public void UncheckAll()
    {
        foreach (var shell in Shells)
            shell.IsChecked = false;
    }

    public ApplyResult Apply()
    {
        var errors = new List<string>();
        string? info = null;
        var settings = _services.Settings;
        var paths = _services.Paths;

        // 1. Data folder
        var oldDirectory = _services.DataDirectory;
        var newDirectory = PathUtil.Normalize(string.IsNullOrWhiteSpace(DataDirectory) ? paths.DefaultDataDirectory : DataDirectory);
        var dataChanged = !PathUtil.AreEqual(newDirectory, oldDirectory);
        if (dataChanged)
        {
            try
            {
                Directory.CreateDirectory(newDirectory);
                var target = Path.Combine(newDirectory, CommandRepository.FileName);
                var source = Path.Combine(oldDirectory, CommandRepository.FileName);
                if (File.Exists(target))
                    info = Strings.Settings_DataSwitched;
                else if (File.Exists(source))
                {
                    File.Copy(source, target);
                    info = Strings.Settings_DataCopied;
                }
                settings.DataDirectory = PathUtil.AreEqual(newDirectory, paths.DefaultDataDirectory) ? null : newDirectory;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{newDirectory}: {e.Message}");
                dataChanged = false;
            }
        }

        // 2. Generated files first, so the profile lines point to something that exists.
        settings.UiLanguage = SelectedUiLanguage.Code;
        settings.AutoReload = AutoReload;
        try
        {
            _services.WriteOutput(_services.Repository.Load().Commands);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            errors.Add(e.Message);
        }

        // 3. Shell integration
        foreach (var shell in Shells)
        {
            try
            {
                if (shell.IsChecked && !shell.IsInstalled)
                    _services.Integration.Install(shell.Target);
                else if (!shell.IsChecked && shell.IsInstalled)
                    _services.Integration.Uninstall(shell.Target);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                errors.Add($"{shell.Label}: {e.Message}");
            }
        }

        settings.EnabledShells = Shells.Where(s => s.IsChecked).Select(s => s.Target).ToList();
        settings.UiLanguage = SelectedUiLanguage.Code;
        settings.SetupCompleted = true;
        try
        {
            _services.SaveSettings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            errors.Add(e.Message);
        }

        return new ApplyResult(dataChanged, errors, info);
    }
}
