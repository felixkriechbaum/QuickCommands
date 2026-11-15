using CommunityToolkit.Mvvm.ComponentModel;
using QuickCommands.App.Resources;
using QuickCommands.Core;

namespace QuickCommands.App.ViewModels;

public sealed record ConflictViewModel(string Message, bool IsError);

/// <summary>
/// One command in the list. Holds its own draft, so switching between commands never loses edits;
/// <see cref="IsDirty"/> compares the draft with the last saved state.
/// </summary>
public sealed partial class CommandItemViewModel : ObservableObject
{
    private QuickCommand? _saved;

    private CommandItemViewModel(Guid id, QuickCommand? saved, string name, ShellLanguage language, string description, string script, bool allowOverride)
    {
        Id = id;
        _saved = saved;
        Name = name;
        Language = LanguageOption.For(language);
        Description = description;
        Script = script;
        AllowOverride = allowOverride;
        Conflicts = [];
    }

    public static CommandItemViewModel FromSaved(QuickCommand command) =>
        new(command.Id, command.Clone(), command.Name, command.Language, command.Description, command.Script, command.AllowOverride);

    public static CommandItemViewModel CreateNew(ShellLanguage language) =>
        new(Guid.NewGuid(), null, "", language, "", ScriptTemplates.For(language), false);

    /// <summary>Raised when name or language changes – both affect conflicts of every command.</summary>
    public event EventHandler? IdentityChanged;

    public Guid Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial LanguageOption Language { get; set; }

    [ObservableProperty]
    public partial string Description { get; set; }

    [ObservableProperty]
    public partial string Script { get; set; }

    [ObservableProperty]
    public partial bool AllowOverride { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ConflictViewModel> Conflicts { get; set; }

    public bool IsNew => _saved is null;

    public bool IsDirty =>
        _saved is null
        || CommandNameRules.Normalize(Name) != _saved.Name
        || Language.Value != _saved.Language
        || Description.Trim() != _saved.Description
        || Script != _saved.Script
        || AllowOverride != _saved.AllowOverride;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Strings.Unnamed : CommandNameRules.Normalize(Name);
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string LanguageBadge => Language.Badge;
    public bool IsPowerShell => Language.Value == ShellLanguage.PowerShell;
    public bool IsSh => Language.Value == ShellLanguage.Sh;

    public bool HasErrors => Conflicts.Any(c => c.IsError);
    public bool HasWarnings => Conflicts.Any(c => !c.IsError);
    public bool ShowOverride => HasWarnings && !HasErrors;
    public bool CanSave => IsDirty && !HasErrors && (!HasWarnings || AllowOverride);

    public QuickCommand ToModel() => new()
    {
        Id = Id,
        Name = CommandNameRules.Normalize(Name),
        Language = Language.Value,
        Description = Description.Trim(),
        Script = Script,
        AllowOverride = HasWarnings && AllowOverride,
    };

    public void MarkSaved(QuickCommand saved)
    {
        _saved = saved.Clone();
        Name = saved.Name;
        Description = saved.Description;
        AllowOverride = saved.AllowOverride;
        RaiseState();
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        RaiseState();
        IdentityChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnLanguageChanged(LanguageOption oldValue, LanguageOption newValue)
    {
        // A new command whose script is still the untouched template gets the template of the new language.
        // oldValue is null only during construction.
        if (oldValue is not null && IsNew && Script == ScriptTemplates.For(oldValue.Value))
            Script = ScriptTemplates.For(newValue.Value);

        OnPropertyChanged(nameof(LanguageBadge));
        OnPropertyChanged(nameof(IsPowerShell));
        OnPropertyChanged(nameof(IsSh));
        RaiseState();
        IdentityChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnDescriptionChanged(string value)
    {
        OnPropertyChanged(nameof(HasDescription));
        RaiseState();
    }

    partial void OnScriptChanged(string value) => RaiseState();

    partial void OnAllowOverrideChanged(bool value) => RaiseState();

    partial void OnConflictsChanged(IReadOnlyList<ConflictViewModel> value)
    {
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(ShowOverride));
        OnPropertyChanged(nameof(CanSave));
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(CanSave));
    }
}
