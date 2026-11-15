using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickCommands.App.Resources;
using QuickCommands.Core;

namespace QuickCommands.App.ViewModels;

/// <summary>Implemented by the main window – keeps windows and dialogs out of the view models.</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText);
    Task ShowMessageAsync(string title, string message);
    Task<bool> RunSetupAsync();

    /// <returns>True if the data folder changed and the commands must be reloaded.</returns>
    Task<bool> ShowSettingsAsync();

    Task<QuickCommand?> PickTemplateAsync();

    /// <returns>True if something was imported or restored and the commands must be reloaded.</returns>
    Task<bool> ShowImportAsync(IReadOnlyList<QuickCommand> existing);

    Task<bool> ShowHistoryAsync(IReadOnlyList<QuickCommand> current);
}

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan SyntaxDelay = TimeSpan.FromMilliseconds(450);

    private readonly AppServices _services;
    private CommandCollection _collection = new();
    private CommandInventory? _inventory;
    private readonly Dictionary<CommandItemViewModel, CancellationTokenSource> _syntaxChecks = [];

    public MainViewModel(AppServices services)
    {
        _services = services;
        SearchText = "";
        SelectedFilter = FilterOption.All[0];
    }

    public IDialogService? Dialogs { get; set; }

    public ObservableCollection<CommandItemViewModel> Items { get; } = [];
    public ObservableCollection<CommandItemViewModel> FilteredItems { get; } = [];
    public IReadOnlyList<FilterOption> FilterOptions => FilterOption.All;
    public IReadOnlyList<LanguageOption> LanguageOptions => LanguageOption.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial CommandItemViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial FilterOption SelectedFilter { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool StatusIsError { get; set; }

    [ObservableProperty]
    public partial bool IsCheckingConflicts { get; set; }

    [ObservableProperty]
    public partial bool HasNoVisibleItems { get; set; }

    public bool HasSelection => Selected is not null;
    public bool HasUnsavedChanges => Items.Any(i => i.IsDirty);

    /// <summary>Called once the main window is shown.</summary>
    public async Task StartAsync()
    {
        if (!_services.Settings.SetupCompleted && Dialogs is not null && !await Dialogs.RunSetupAsync())
            return;

        Load();
        try
        {
            // Keep generated files in sync, e.g. after commands.json was changed on another computer.
            _services.WriteOutput(_collection.Commands);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatus(string.Format(Strings.Status_Error, e.Message), isError: true);
        }
        await LoadInventoryAsync();
        await CheckAllSyntaxAsync();
    }

    public void Load()
    {
        foreach (var item in Items)
            Detach(item);
        Items.Clear();

        try
        {
            _collection = _services.Repository.Load();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _collection = new CommandCollection();
            SetStatus(string.Format(Strings.Status_Error, e.Message), isError: true);
        }

        foreach (var command in _collection.Commands)
            AddItem(CommandItemViewModel.FromSaved(command));

        RefreshFilter();
        Selected = FilteredItems.FirstOrDefault();
        RecheckAll();
    }

    private async Task LoadInventoryAsync()
    {
        IsCheckingConflicts = true;
        try
        {
            _inventory = await _services.Inventory;
        }
        catch (Exception)
        {
            // Without inventory only the static rules are checked – not worth bothering the user.
            _inventory = CommandInventory.Empty;
        }
        finally
        {
            IsCheckingConflicts = false;
        }
        RecheckAll();
    }

    [RelayCommand]
    private void New() => Open(CommandItemViewModel.CreateNew(SelectedFilter.Value ?? DefaultLanguage()));

    [RelayCommand]
    private async Task FromTemplateAsync()
    {
        if (Dialogs is null || await Dialogs.PickTemplateAsync() is not { } template)
            return;
        Open(CommandItemViewModel.CreateFrom(template));
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (Selected is not { } item)
            return;
        var name = CommandNameRules.Normalize(item.Name);
        var copyName = name.Length == 0 ? "" : UniqueName(name + "-copy", item.Language.Value);
        Open(CommandItemViewModel.CreateFrom(item.ToDraft(copyName)));
    }

    private string UniqueName(string name, ShellLanguage language)
    {
        var taken = Items.Where(i => i.Language.Value == language).Select(i => CommandNameRules.Normalize(i.Name)).ToHashSet();
        var candidate = name;
        for (var n = 2; taken.Contains(candidate); n++)
            candidate = name + n;
        return candidate;
    }

    private void Open(CommandItemViewModel item)
    {
        AddItem(item);
        SearchText = "";
        RefreshFilter();
        Selected = item;
        RecheckAll();
        _ = CheckSyntaxAsync(item, TimeSpan.Zero);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var item = Selected;
        if (item is null)
            return;

        if (_inventory is null)
        {
            // Saving right after start: wait for the inventory so no conflict slips through.
            await LoadInventoryAsync();
        }
        if (!item.CanSave)
            return;

        var model = item.ToModel();
        var index = _collection.Commands.FindIndex(c => c.Id == model.Id);
        var previous = index >= 0 ? _collection.Commands[index] : null;
        if (index >= 0)
            _collection.Commands[index] = model;
        else
            _collection.Commands.Add(model);

        try
        {
            _services.Repository.Save(_collection);
            _services.WriteOutput(_collection.Commands);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (previous is not null)
                _collection.Commands[index] = previous;
            else
                _collection.Commands.Remove(model);
            SetStatus(string.Format(Strings.Status_Error, e.Message), isError: true);
            return;
        }

        item.MarkSaved(model);
        RefreshFilter();
        Selected = item;

        string message;
        if (!_services.IsLanguageSetUp(model.Language))
            message = string.Format(Strings.Status_Saved_NotInstalled, LanguageOption.For(model.Language).Label);
        else if (model.Language == ShellLanguage.Cmd)
            message = Strings.Status_Saved_Cmd;
        else
            message = _services.Settings.AutoReload ? Strings.Status_Saved_AutoReload : Strings.Status_Saved_Shell;
        if (item.HasSyntaxErrors)
            message = string.Format(Strings.Status_Saved_SyntaxWarning, item.SyntaxErrors[0].Line);
        SetStatus(message, isError: item.HasSyntaxErrors);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var item = Selected;
        if (item is null)
            return;

        var needsConfirmation = !item.IsNew || !string.IsNullOrWhiteSpace(item.Name);
        if (needsConfirmation && Dialogs is not null &&
            !await Dialogs.ConfirmAsync(Strings.Confirm_Delete_Title,
                string.Format(Strings.Confirm_Delete_Message, item.DisplayName), Strings.Action_Delete))
            return;

        var removed = _collection.Commands.RemoveAll(c => c.Id == item.Id) > 0;
        if (removed)
        {
            try
            {
                _services.Repository.Save(_collection);
                _services.WriteOutput(_collection.Commands);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                SetStatus(string.Format(Strings.Status_Error, e.Message), isError: true);
                Load();
                return;
            }
        }

        var position = FilteredItems.IndexOf(item);
        Detach(item);
        Items.Remove(item);
        RefreshFilter();
        Selected = FilteredItems.Count == 0 ? null : FilteredItems[Math.Clamp(position, 0, FilteredItems.Count - 1)];
        RecheckAll();
        if (removed)
            SetStatus(string.Format(Strings.Status_Deleted, item.DisplayName), isError: false);
    }

    /// <summary>"Try it": the current draft, unsaved, in a new terminal.</summary>
    [RelayCommand]
    private void Try()
    {
        var item = Selected;
        if (item is null)
            return;
        if (!item.CanTry)
        {
            SetStatus(Strings.Try_InvalidName, isError: true);
            return;
        }
        try
        {
            var settings = _services.Settings;
            var trial = _services.Trials.Prepare(item.ToModel(), item.TryArguments, _services.GeneratorOptions.Texts,
                preferZsh: settings.EnabledShells.Contains(ShellTarget.Zsh),
                preferWindowsPowerShell: settings.EnabledShells.Contains(ShellTarget.WindowsPowerShell) && !settings.EnabledShells.Contains(ShellTarget.Pwsh));
            _services.Trials.Launch(trial);
            SetStatus(string.Format(Strings.Status_Tried, (item.DisplayName + " " + item.TryArguments).Trim()), isError: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            SetStatus(string.Format(Strings.Status_TryFailed, e.Message), isError: true);
        }
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (Dialogs is null)
            return;
        if (await Dialogs.ShowSettingsAsync())
            Load();
        RecheckAll();
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (Dialogs is null)
            return;
        if (_inventory is null)
            await LoadInventoryAsync();
        if (await Dialogs.ShowImportAsync(_collection.Commands))
            await ReloadKeepingDraftsAsync();
    }

    [RelayCommand]
    private async Task HistoryAsync()
    {
        if (Dialogs is null)
            return;
        if (HasUnsavedChanges &&
            !await Dialogs.ConfirmAsync(Strings.Confirm_Discard_Title, Strings.Confirm_Discard_Message, Strings.Confirm_Discard_Action))
            return;
        if (await Dialogs.ShowHistoryAsync(_collection.Commands))
        {
            Load();
            await CheckAllSyntaxAsync();
            SetStatus(Strings.Status_Restored, isError: false);
        }
    }

    /// <summary>After an import: reload saved commands, but keep unsaved new drafts.</summary>
    private async Task ReloadKeepingDraftsAsync()
    {
        var drafts = Items.Where(i => i.IsNew).ToList();
        var before = _collection.Commands.Count;
        Load();
        foreach (var draft in drafts)
            AddItem(draft);
        RefreshFilter();
        RecheckAll();
        SetStatus(string.Format(Strings.Status_Imported, _collection.Commands.Count - before), isError: false);
        await CheckAllSyntaxAsync();
    }

    /// <summary>Checks every command once in the background, so syntax errors show up in the list.</summary>
    private async Task CheckAllSyntaxAsync()
    {
        foreach (var item in Items.ToList())
            await CheckSyntaxAsync(item, TimeSpan.Zero);
    }

    private async Task CheckSyntaxAsync(CommandItemViewModel item, TimeSpan delay)
    {
        if (_syntaxChecks.TryGetValue(item, out var running))
            running.Cancel();
        var cts = _syntaxChecks[item] = new CancellationTokenSource();
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cts.Token);
            var language = item.Language.Value;
            var script = item.Script;
            var result = await _services.Syntax.CheckAsync(language, script, cts.Token);
            if (item.Language.Value == language && item.Script == script)
                item.SyntaxErrors = result?.Errors ?? [];
        }
        catch (OperationCanceledException)
        {
        }
    }

    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnSelectedFilterChanged(FilterOption value) => RefreshFilter();

    partial void OnSelectedChanged(CommandItemViewModel? value)
    {
        if (value is not null && StatusIsError is false)
            StatusMessage = null;
    }

    private void AddItem(CommandItemViewModel item)
    {
        item.IdentityChanged += OnIdentityChanged;
        item.ScriptChanged += OnScriptChanged;
        Items.Add(item);
    }

    private void Detach(CommandItemViewModel item)
    {
        item.IdentityChanged -= OnIdentityChanged;
        item.ScriptChanged -= OnScriptChanged;
        if (_syntaxChecks.Remove(item, out var running))
            running.Cancel();
    }

    private void OnIdentityChanged(object? sender, EventArgs e) => RecheckAll();

    private void OnScriptChanged(object? sender, EventArgs e)
    {
        if (sender is CommandItemViewModel item)
            _ = CheckSyntaxAsync(item, SyntaxDelay);
    }

    private ShellLanguage DefaultLanguage()
    {
        var enabled = _services.Settings.EnabledShells;
        if (enabled.Count > 0)
            return enabled[0].Language();
        return OperatingSystem.IsWindows() ? ShellLanguage.PowerShell : ShellLanguage.Sh;
    }

    /// <summary>
    /// Updates the visible list in place (no Clear), so the ListBox keeps its selection while filtering.
    /// New commands stay on top, the rest is sorted by name.
    /// </summary>
    private void RefreshFilter()
    {
        var search = SearchText.Trim();
        var target = Items
            .Where(i => SelectedFilter.Matches(i.Language.Value))
            .Where(i => search.Length == 0
                        || i.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || i.Description.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.IsNew ? 0 : 1)
            .ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Language.Value)
            .ToList();

        var selected = Selected;
        for (var i = FilteredItems.Count - 1; i >= 0; i--)
        {
            if (!target.Contains(FilteredItems[i]))
                FilteredItems.RemoveAt(i);
        }
        for (var i = 0; i < target.Count; i++)
        {
            var current = FilteredItems.IndexOf(target[i]);
            if (current == i)
                continue;
            if (current >= 0)
                FilteredItems.RemoveAt(current);
            FilteredItems.Insert(i, target[i]);
        }

        if (selected is not null && FilteredItems.Contains(selected) && Selected != selected)
            Selected = selected;
        HasNoVisibleItems = FilteredItems.Count == 0;
    }

    private void RecheckAll()
    {
        var checker = new ConflictChecker(_inventory);
        var keys = Items.Select(i => new CommandKey(i.Id, i.Name, i.Language.Value)).ToList();
        foreach (var item in Items)
        {
            item.Conflicts = checker
                .Check(item.Name, item.Language.Value, item.Id, keys)
                .Select(c => new ConflictViewModel(Describe(c, CommandNameRules.Normalize(item.Name)), c.Severity == ConflictSeverity.Error))
                .ToList();
        }
    }

    internal static string Describe(Conflict conflict, string name)
    {
        var format = conflict.Kind switch
        {
            ConflictKind.EmptyName => Strings.Conflict_EmptyName,
            ConflictKind.InvalidName => Strings.Conflict_InvalidName,
            ConflictKind.ReservedName => Strings.Conflict_ReservedName,
            ConflictKind.Duplicate => Strings.Conflict_Duplicate,
            ConflictKind.CmdBuiltin => Strings.Conflict_CmdBuiltin,
            ConflictKind.ShadowedBySystemPath => Strings.Conflict_ShadowedBySystemPath,
            ConflictKind.ExistingExecutable => Strings.Conflict_ExistingExecutable,
            ConflictKind.ShellKeyword => Strings.Conflict_ShellKeyword,
            ConflictKind.CriticalCommand => Strings.Conflict_CriticalCommand,
            ConflictKind.ShellBuiltin => Strings.Conflict_ShellBuiltin,
            ConflictKind.ExistingPowerShellCommand => Strings.Conflict_ExistingPowerShellCommand,
            _ => conflict.Kind.ToString(),
        };
        return string.Format(format, name, conflict.Detail);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusIsError = isError;
        StatusMessage = message;
    }
}
