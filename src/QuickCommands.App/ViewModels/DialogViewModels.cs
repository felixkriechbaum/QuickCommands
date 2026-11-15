using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using QuickCommands.App.Resources;
using QuickCommands.Core;

namespace QuickCommands.App.ViewModels;

// ---------- Templates ----------

public sealed class TemplateItemViewModel(CommandTemplate template, string languageCode)
{
    public CommandTemplate Template { get; } = template;
    public QuickCommand Command { get; } = template.ToCommand(languageCode);
    public string Name => Template.Name;
    public string Description => Command.Description;
    public string Badge => LanguageOption.For(Template.Language).Badge;
    public bool IsPowerShell => Template.Language == ShellLanguage.PowerShell;
    public bool IsSh => Template.Language == ShellLanguage.Sh;
    public string Script => Command.Script.TrimEnd();
    public string Help => string.Join(Environment.NewLine, HelpText.Lines(Command, TerminalTexts.For(languageCode), includeLanguage: false).Skip(1)).Trim();
}

public sealed partial class TemplatesViewModel : ObservableObject
{
    private readonly List<TemplateItemViewModel> _all;

    public TemplatesViewModel(AppServices services)
    {
        // Languages of the shells that are set up come first.
        var enabled = services.Settings.EnabledShells.Select(s => s.Language()).ToHashSet();
        _all = TemplateLibrary.All
            .Where(t => t.Language != ShellLanguage.Cmd || OperatingSystem.IsWindows())
            .OrderBy(t => enabled.Contains(t.Language) ? 0 : 1)
            .ThenBy(t => t.Language)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new TemplateItemViewModel(t, services.LanguageCode))
            .ToList();
        SearchText = "";
        Refresh();
        Selected = Items.FirstOrDefault();
    }

    public ObservableCollection<TemplateItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial TemplateItemViewModel? Selected { get; set; }

    public bool HasSelection => Selected is not null;

    partial void OnSearchTextChanged(string value) => Refresh();

    private void Refresh()
    {
        var search = SearchText.Trim();
        Items.Clear();
        foreach (var t in _all.Where(t => search.Length == 0
                                          || t.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                                          || t.Description.Contains(search, StringComparison.OrdinalIgnoreCase)))
            Items.Add(t);
        if (Selected is null || !Items.Contains(Selected))
            Selected = Items.FirstOrDefault();
    }
}

// ---------- Import ----------

public sealed partial class ImportCandidateViewModel : ObservableObject
{
    public ImportCandidateViewModel(ImportCandidate candidate, string? blockedReason, bool hasWarnings)
    {
        Candidate = candidate;
        BlockedReason = blockedReason;
        HasWarnings = hasWarnings;
        IsChecked = blockedReason is null;
    }

    public ImportCandidate Candidate { get; }
    public string? BlockedReason { get; }
    public bool HasWarnings { get; }
    public bool IsEnabled => BlockedReason is null;
    public string Name => Candidate.Name;
    public string Badge => LanguageOption.For(Candidate.Language).Badge;
    public bool IsPowerShell => Candidate.Language == ShellLanguage.PowerShell;
    public bool IsSh => Candidate.Language == ShellLanguage.Sh;
    public string Details => (Candidate.IsAlias ? Strings.Import_Alias : Strings.Import_Function) + " · " +
                             Path.GetFileName(Candidate.SourceFile) + ":" + Candidate.StartLine +
                             (BlockedReason is null ? "" : " · " + BlockedReason);

    [ObservableProperty]
    public partial bool IsChecked { get; set; }
}

public sealed record ImportResult(int Count, IReadOnlyList<string> Backups, IReadOnlyList<string> NotCommentedOut);

public sealed partial class ImportViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IReadOnlyList<QuickCommand> _existing;

    public ImportViewModel(AppServices services, IReadOnlyList<QuickCommand> existing)
    {
        _services = services;
        _existing = existing;
        IsScanning = true;
        CommentOut = true;
    }

    public ObservableCollection<ImportCandidateViewModel> Candidates { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNone))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial bool CommentOut { get; set; }

    [ObservableProperty]
    public partial ImportCandidateViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial string? Notice { get; set; }

    public bool HasNone => !IsScanning && Candidates.Count == 0;
    public int CheckedCount => Candidates.Count(c => c.IsChecked && c.IsEnabled);
    public string ImportLabel => string.Format(Strings.Import_Action, CheckedCount);
    public bool CanImport => CheckedCount > 0;

    public async Task LoadAsync()
    {
        var hasPowerShell = ShellDetector.FindExecutable("pwsh") is not null
                            || (OperatingSystem.IsWindows() && ShellDetector.FindExecutable("powershell") is not null);
        if (!hasPowerShell && _services.Importer.ProfileFiles(ShellLanguage.PowerShell).Any())
            Notice = Strings.Import_NoPowerShell;

        IReadOnlyList<ImportCandidate> found;
        try
        {
            found = await _services.Importer.ScanAsync(hasPowerShell ? _services.Syntax.PowerShell : null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Notice = e.Message;
            found = [];
        }

        CommandInventory? inventory = null;
        try
        {
            inventory = await _services.Inventory;
        }
        catch (Exception)
        {
            // Static rules only.
        }

        var checker = new ConflictChecker(inventory);
        var keys = _existing.Select(c => new CommandKey(c.Id, c.Name, c.Language)).ToList();
        foreach (var candidate in found)
        {
            var conflicts = checker.Check(candidate.Name, candidate.Language, Guid.NewGuid(), keys);
            var error = conflicts.FirstOrDefault(c => c.Severity == ConflictSeverity.Error);
            string? blocked = error switch
            {
                null => null,
                { Kind: ConflictKind.Duplicate } => Strings.Import_Exists,
                { Kind: ConflictKind.InvalidName or ConflictKind.EmptyName or ConflictKind.ReservedName } => Strings.Import_Invalid,
                _ => MainViewModel.Describe(error, candidate.Name),
            };
            var vm = new ImportCandidateViewModel(candidate, blocked, conflicts.Any(c => c.Severity == ConflictSeverity.Warning));
            vm.PropertyChanged += (_, _) => RaiseCount();
            Candidates.Add(vm);
            if (blocked is null)
                keys.Add(new CommandKey(Guid.NewGuid(), candidate.Name, candidate.Language));
        }
        Selected = Candidates.FirstOrDefault();
        IsScanning = false;
        RaiseCount();
    }

    private void RaiseCount()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(ImportLabel));
        OnPropertyChanged(nameof(CanImport));
    }

    public ImportResult Import()
    {
        var chosen = Candidates.Where(c => c.IsChecked && c.IsEnabled).ToList();
        var repository = _services.Repository;
        var collection = repository.Load();
        foreach (var c in chosen)
        {
            collection.Commands.Add(new QuickCommand
            {
                Name = c.Candidate.Name,
                Language = c.Candidate.Language,
                Script = c.Candidate.Script + "\n",
                // The user had it in the profile, so overriding an existing command was deliberate.
                AllowOverride = c.HasWarnings,
            });
        }
        repository.Save(collection);
        _services.WriteOutput(collection.Commands);

        IReadOnlyList<string> backups = [];
        var notCommented = new List<string>();
        if (CommentOut)
        {
            backups = _services.Importer.CommentOut(chosen.Select(c => c.Candidate));
            notCommented.AddRange(chosen.Where(c => !c.Candidate.CanCommentOut).Select(c => c.Name));
        }
        return new ImportResult(chosen.Count, backups, notCommented);
    }
}

// ---------- History ----------

public sealed record HistoryEntryViewModel(HistoryEntry Entry, CommandCollection State, string Label, string Summary);

public sealed record HistoryCommandViewModel(QuickCommand Command, ChangeKind Change, string StateLabel)
{
    public string Name => Command.Name;
    public string Badge => LanguageOption.For(Command.Language).Badge;
    public bool IsPowerShell => Command.Language == ShellLanguage.PowerShell;
    public bool IsSh => Command.Language == ShellLanguage.Sh;
    public bool IsDifferent => Change != ChangeKind.Unchanged;
}

public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly CommandCollection _current;

    public HistoryViewModel(AppServices services, IReadOnlyList<QuickCommand> current)
    {
        _services = services;
        _current = new CommandCollection { Commands = current.Select(c => c.Clone()).ToList() };

        var history = services.Repository.History;
        var states = new List<(HistoryEntry Entry, CommandCollection State)>();
        foreach (var entry in history.List())
        {
            try
            {
                states.Add((entry, history.Load(entry)));
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
            {
                // A broken snapshot is skipped.
            }
        }
        for (var i = 0; i < states.Count; i++)
        {
            var older = i + 1 < states.Count ? states[i + 1].State : null;
            var diff = HistoryDiff.Compare(older, states[i].State);
            var summary = $"+{diff.Count(d => d.Kind == ChangeKind.Added)}  ~{diff.Count(d => d.Kind == ChangeKind.Changed)}  " +
                          $"−{diff.Count(d => d.Kind == ChangeKind.Removed)}   ·   {string.Format(Strings.History_Count, states[i].State.Commands.Count)}";
            var label = states[i].Entry.TimestampUtc.ToLocalTime().ToString("g", CultureInfo.CurrentUICulture);
            Entries.Add(new HistoryEntryViewModel(states[i].Entry, states[i].State, label, summary));
        }
        Selected = Entries.FirstOrDefault();
    }

    public ObservableCollection<HistoryEntryViewModel> Entries { get; } = [];
    public ObservableCollection<HistoryCommandViewModel> Commands { get; } = [];
    public bool IsEmpty => Entries.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestoreAll))]
    public partial HistoryEntryViewModel? Selected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestoreCommand), nameof(Preview))]
    public partial HistoryCommandViewModel? SelectedCommand { get; set; }

    public bool CanRestoreAll => Selected is not null;
    public bool CanRestoreCommand => SelectedCommand is { Change: ChangeKind.Added or ChangeKind.Changed };
    public string Preview => SelectedCommand?.Command.Script.TrimEnd() ?? "";

    partial void OnSelectedChanged(HistoryEntryViewModel? value)
    {
        Commands.Clear();
        if (value is null)
            return;
        // Compared with today: "Added" means it only exists in the snapshot, i.e. it was deleted since.
        foreach (var change in HistoryDiff.Compare(_current, value.State))
        {
            var label = change.Kind switch
            {
                ChangeKind.Added => Strings.History_State_Deleted,
                ChangeKind.Changed => Strings.History_State_Changed,
                ChangeKind.Removed => Strings.History_State_New,
                _ => Strings.History_State_Same,
            };
            Commands.Add(new HistoryCommandViewModel(change.Command, change.Kind, label));
        }
        SelectedCommand = Commands.FirstOrDefault(c => c.IsDifferent) ?? Commands.FirstOrDefault();
    }

    /// <returns>An error message, or null on success.</returns>
    public string? RestoreCommand()
    {
        if (SelectedCommand is not { } selected || !CanRestoreCommand)
            return null;
        var repository = _services.Repository;
        var collection = repository.Load();
        var restored = selected.Command.Clone();
        var index = collection.Commands.FindIndex(c => c.Id == restored.Id);
        if (index >= 0)
        {
            collection.Commands[index] = restored;
        }
        else
        {
            if (collection.Commands.Any(c => c.Language == restored.Language && c.Name == restored.Name))
                return string.Format(Strings.History_NameTaken, restored.Name);
            collection.Commands.Add(restored);
        }
        repository.Save(collection);
        _services.WriteOutput(collection.Commands);
        return null;
    }

    public void RestoreAll()
    {
        if (Selected is null)
            return;
        var state = new CommandCollection { Commands = Selected.State.Commands.Select(c => c.Clone()).ToList() };
        _services.Repository.Save(state);
        _services.WriteOutput(state.Commands);
    }
}
