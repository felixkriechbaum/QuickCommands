using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickCommands.App.Resources;
using QuickCommands.Core;

namespace QuickCommands.App.ViewModels;

public sealed record ConflictViewModel(string Message, bool IsError);

/// <summary>One row of the parameter table.</summary>
public sealed partial class ParameterItemViewModel : ObservableObject
{
    public ParameterItemViewModel(CommandParameter p)
    {
        Name = p.Name;
        Kind = KindOption.For(p.Kind);
        Required = p.Required;
        Default = p.Default;
        ChoicesText = string.Join('|', p.Choices);
        Description = p.Description;
    }

    public event EventHandler? Changed;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoice), nameof(CanBeRequired), nameof(CanHaveDefault))]
    public partial KindOption Kind { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanHaveDefault))]
    public partial bool Required { get; set; }

    [ObservableProperty]
    public partial string Default { get; set; }

    [ObservableProperty]
    public partial string ChoicesText { get; set; }

    [ObservableProperty]
    public partial string Description { get; set; }

    public IReadOnlyList<KindOption> KindOptions => KindOption.All;
    public bool IsChoice => Kind.Value == ParameterKind.Choice;
    public bool CanBeRequired => Kind.Value != ParameterKind.Switch;
    public bool CanHaveDefault => Kind.Value != ParameterKind.Switch && !Required;

    public CommandParameter ToModel() => new()
    {
        Name = ParameterRules.NormalizeName(Name),
        Kind = Kind.Value,
        Required = Kind.Value != ParameterKind.Switch && Required,
        Default = Kind.Value == ParameterKind.Switch ? "" : Default.Trim(),
        Choices = Kind.Value == ParameterKind.Choice ? ParameterRules.ParseChoices(ChoicesText) : [],
        Description = Description.Trim(),
    };

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// One command in the list. Holds its own draft, so switching between commands never loses edits;
/// <see cref="IsDirty"/> compares the draft with the last saved state.
/// </summary>
public sealed partial class CommandItemViewModel : ObservableObject
{
    private QuickCommand? _saved;

    private CommandItemViewModel(Guid id, QuickCommand? saved, QuickCommand draft)
    {
        Id = id;
        _saved = saved;
        Name = draft.Name;
        Language = LanguageOption.For(draft.Language);
        Description = draft.Description;
        Script = draft.Script;
        AllowOverride = draft.AllowOverride;
        TryArguments = "";
        Conflicts = [];
        ParameterProblems = [];
        SyntaxErrors = [];
        Parameters.CollectionChanged += OnParametersChanged;
        foreach (var p in draft.Parameters)
            Parameters.Add(new ParameterItemViewModel(p));
        ValidateParameters();
    }

    public static CommandItemViewModel FromSaved(QuickCommand command) => new(command.Id, command.Clone(), command.Clone());

    public static CommandItemViewModel CreateNew(ShellLanguage language) =>
        new(Guid.NewGuid(), null, new QuickCommand { Language = language, Script = ScriptTemplates.For(language) });

    /// <summary>A new, unsaved command prefilled from a template, a copy or an import.</summary>
    public static CommandItemViewModel CreateFrom(QuickCommand draft) => new(Guid.NewGuid(), null, draft.Clone());

    /// <summary>Raised when name or language changes – both affect conflicts of every command.</summary>
    public event EventHandler? IdentityChanged;

    /// <summary>Raised when the script or language changes, so the syntax check can run again.</summary>
    public event EventHandler? ScriptChanged;

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
    public partial string TryArguments { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ConflictViewModel> Conflicts { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ConflictViewModel> ParameterProblems { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<SyntaxError> SyntaxErrors { get; set; }

    public ObservableCollection<ParameterItemViewModel> Parameters { get; } = [];

    public bool IsNew => _saved is null;

    public bool IsDirty =>
        _saved is null
        || CommandNameRules.Normalize(Name) != _saved.Name
        || Language.Value != _saved.Language
        || Description.Trim() != _saved.Description
        || Script != _saved.Script
        || AllowOverride != _saved.AllowOverride
        || !ParametersEqual(Parameters.Select(p => p.ToModel()).ToList(), _saved.Parameters);

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Strings.Unnamed : CommandNameRules.Normalize(Name);
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string LanguageBadge => Language.Badge;
    public bool IsPowerShell => Language.Value == ShellLanguage.PowerShell;
    public bool IsSh => Language.Value == ShellLanguage.Sh;
    public bool HasParameters => Parameters.Count > 0;

    public IEnumerable<ConflictViewModel> Messages => Conflicts.Concat(ParameterProblems);
    public bool HasErrors => Conflicts.Any(c => c.IsError) || ParameterProblems.Count > 0;
    public bool HasWarnings => Conflicts.Any(c => !c.IsError);
    public bool ShowOverride => HasWarnings && !HasErrors;
    public bool CanSave => IsDirty && !HasErrors && (!HasWarnings || AllowOverride);

    public bool HasSyntaxErrors => SyntaxErrors.Count > 0;

    public string SyntaxMessage
    {
        get
        {
            if (SyntaxErrors.Count == 0)
                return "";
            var first = SyntaxErrors[0];
            var text = string.Format(Strings.Syntax_Error, first.Line, first.Message);
            return SyntaxErrors.Count > 1 ? text + " " + string.Format(Strings.Syntax_More, SyntaxErrors.Count - 1) : text;
        }
    }

    public bool CanTry => CommandNameRules.IsValid(CommandNameRules.Normalize(Name)) && ParameterProblems.Count == 0;

    /// <summary>The usage line as `name --help` will print it.</summary>
    public string UsagePreview => HasParameters ? string.Format(Strings.Param_Usage, HelpText.UsageLine(ToModel())) : "";

    /// <summary>The command as you would type it, shown as a terminal line under the parameters.</summary>
    public string UsageLine => HelpText.UsageLine(ToModel());
    public bool ShowUsageLine => CommandNameRules.IsValid(CommandNameRules.Normalize(Name));

    /// <summary>How the script reaches the parameters and the remaining arguments.</summary>
    public string VariableHint
    {
        get
        {
            var rest = Language.Value switch
            {
                ShellLanguage.PowerShell => "$args",
                ShellLanguage.Cmd => "%*",
                _ => "\"$@\"",
            };
            if (!HasParameters)
                return string.Format(Strings.Param_None, rest);
            var names = Parameters.Select(p => ParameterRules.NormalizeName(p.Name)).Where(n => n.Length > 0)
                .Select(n => Language.Value == ShellLanguage.Cmd ? "%" + n + "%" : "$" + n);
            return string.Format(Strings.Param_Variables, string.Join(", ", names), rest);
        }
    }

    public QuickCommand ToModel() => new()
    {
        Id = Id,
        Name = CommandNameRules.Normalize(Name),
        Language = Language.Value,
        Description = Description.Trim(),
        Script = Script,
        AllowOverride = HasWarnings && AllowOverride,
        Parameters = Parameters.Select(p => p.ToModel()).ToList(),
    };

    /// <summary>The draft as it is, for duplicating – a copy gets its own name.</summary>
    public QuickCommand ToDraft(string name)
    {
        var model = ToModel();
        model.Name = name;
        model.AllowOverride = false;
        return model;
    }

    public void MarkSaved(QuickCommand saved)
    {
        _saved = saved.Clone();
        Name = saved.Name;
        Description = saved.Description;
        AllowOverride = saved.AllowOverride;
        RaiseState();
    }

    [RelayCommand]
    private void AddParameter() => Parameters.Add(new ParameterItemViewModel(new CommandParameter { Name = "" }));

    [RelayCommand]
    private void RemoveParameter(ParameterItemViewModel? parameter)
    {
        if (parameter is not null)
            Parameters.Remove(parameter);
    }

    private static bool ParametersEqual(IReadOnlyList<CommandParameter> a, IReadOnlyList<CommandParameter> b) =>
        a.Count == b.Count && a.Zip(b).All(x => x.First.ContentEquals(x.Second));

    private void OnParametersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (ParameterItemViewModel p in e.OldItems ?? Array.Empty<ParameterItemViewModel>())
            p.Changed -= OnParameterChanged;
        foreach (ParameterItemViewModel p in e.NewItems ?? Array.Empty<ParameterItemViewModel>())
            p.Changed += OnParameterChanged;
        OnPropertyChanged(nameof(HasParameters));
        OnParameterChanged(this, EventArgs.Empty);
    }

    private void OnParameterChanged(object? sender, EventArgs e)
    {
        ValidateParameters();
        OnPropertyChanged(nameof(UsagePreview));
        OnPropertyChanged(nameof(UsageLine));
        OnPropertyChanged(nameof(VariableHint));
        RaiseState();
    }

    private void ValidateParameters()
    {
        var models = Parameters.Select(p => p.ToModel()).ToList();
        ParameterProblems = ParameterRules.Validate(Language.Value, models, Script)
            .Select(i => new ConflictViewModel(DescribeIssue(i), IsError: true))
            .ToList();
    }

    private static string DescribeIssue(ParameterIssue issue)
    {
        var format = issue.Kind switch
        {
            ParameterIssueKind.EmptyName => Strings.ParamIssue_EmptyName,
            ParameterIssueKind.InvalidName => Strings.ParamIssue_InvalidName,
            ParameterIssueKind.DuplicateName => Strings.ParamIssue_DuplicateName,
            ParameterIssueKind.ReservedName => Strings.ParamIssue_ReservedName,
            ParameterIssueKind.ShellVariable => Strings.ParamIssue_ShellVariable,
            ParameterIssueKind.NoChoices => Strings.ParamIssue_NoChoices,
            ParameterIssueKind.InvalidChoice => Strings.ParamIssue_InvalidChoice,
            ParameterIssueKind.DefaultNotInChoices => Strings.ParamIssue_DefaultNotInChoices,
            ParameterIssueKind.InvalidDefault => Strings.ParamIssue_InvalidDefault,
            ParameterIssueKind.ScriptHasParamBlock => Strings.ParamIssue_ScriptHasParamBlock,
            _ => issue.Kind.ToString(),
        };
        return string.Format(format, issue.Index + 1, issue.Detail);
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(ShowUsageLine));
        OnPropertyChanged(nameof(UsagePreview));
        OnPropertyChanged(nameof(UsageLine));
        OnPropertyChanged(nameof(CanTry));
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
        OnPropertyChanged(nameof(VariableHint));
        if (oldValue is not null)
        {
            ValidateParameters();
            SyntaxErrors = [];
            ScriptChanged?.Invoke(this, EventArgs.Empty);
        }
        RaiseState();
        IdentityChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnDescriptionChanged(string value)
    {
        OnPropertyChanged(nameof(HasDescription));
        RaiseState();
    }

    partial void OnScriptChanged(string value)
    {
        if (Parameters is not null && Language is not null)
        {
            ValidateParameters();
            ScriptChanged?.Invoke(this, EventArgs.Empty);
        }
        RaiseState();
    }

    partial void OnAllowOverrideChanged(bool value) => RaiseState();

    partial void OnConflictsChanged(IReadOnlyList<ConflictViewModel> value) => RaiseErrors();

    partial void OnParameterProblemsChanged(IReadOnlyList<ConflictViewModel> value)
    {
        OnPropertyChanged(nameof(CanTry));
        RaiseErrors();
    }

    partial void OnSyntaxErrorsChanged(IReadOnlyList<SyntaxError> value)
    {
        OnPropertyChanged(nameof(HasSyntaxErrors));
        OnPropertyChanged(nameof(SyntaxMessage));
    }

    private void RaiseErrors()
    {
        OnPropertyChanged(nameof(Messages));
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
