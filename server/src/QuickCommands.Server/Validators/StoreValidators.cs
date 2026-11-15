using System.Text;
using System.Text.Json;
using FluentValidation;
using QuickCommands.Server.Dtos.Requests;

namespace QuickCommands.Server.Validators;

public sealed class PackageRequestValidator : AbstractValidator<PackageRequest>
{
    public PackageRequestValidator()
    {
        RuleFor(r => r.Title).NotEmpty()
            .Must(t => t.Trim().Length is >= 3 and <= 80).WithMessage("The title must be 3–80 characters long.");
        RuleFor(r => r.Description).MaximumLength(2000);
        RuleFor(r => r.Category).NotEmpty();

        RuleFor(r => r.Tags)
            .Must(t => StoreRules.NormalizeTags(t).Count <= StoreRules.MaxTagsPerPackage)
            .WithMessage($"At most {StoreRules.MaxTagsPerPackage} tags are allowed.");
        RuleForEach(r => r.Tags)
            .Must(t => StoreRules.IsValidTag(StoreRules.NormalizeTag(t)))
            .WithMessage("Tags must be 2–24 characters: a-z, 0-9 or '-'.");

        RuleFor(r => r.Commands).NotEmpty()
            .Must(c => c.Count <= StoreRules.MaxCommandsPerPackage)
            .WithMessage($"A package can contain at most {StoreRules.MaxCommandsPerPackage} commands.");
        RuleFor(r => r.Commands)
            .Must(HaveUniqueNamesPerLanguage)
            .When(r => r.Commands is not null)
            .WithMessage("Each command name may only appear once per language.");
        RuleForEach(r => r.Commands).SetValidator(new PackageCommandRequestValidator());
    }

    private static bool HaveUniqueNamesPerLanguage(IReadOnlyList<PackageCommandRequest> commands) =>
        commands.Where(c => c is not null)
            .GroupBy(c => (StoreRules.NormalizeCommandName(c.Name), c.Language))
            .All(g => g.Count() == 1);
}

public sealed class PackageCommandRequestValidator : AbstractValidator<PackageCommandRequest>
{
    public PackageCommandRequestValidator()
    {
        RuleFor(c => c.Name)
            .Must(n => StoreRules.IsValidCommandName(StoreRules.NormalizeCommandName(n)))
            .WithMessage("The command name must start with a-z or 0-9, contain only a-z, 0-9, '_' or '-', be at most 64 characters and not be 'qc'.");
        RuleFor(c => c.Language).IsInEnum();
        RuleFor(c => c.Description).MaximumLength(500);
        RuleFor(c => c.Script).NotEmpty()
            .Must(s => Encoding.UTF8.GetByteCount(s) <= StoreRules.MaxScriptBytes)
            .WithMessage($"The script must not exceed {StoreRules.MaxScriptBytes / 1024} KB.");
        RuleFor(c => c.Parameters)
            .Must(p => p!.Value.ValueKind == JsonValueKind.Array).WithMessage("Parameters must be a JSON array.")
            .Must(p => Encoding.UTF8.GetByteCount(p!.Value.GetRawText()) <= StoreRules.MaxParametersBytes)
            .WithMessage($"Parameters must not exceed {StoreRules.MaxParametersBytes / 1024} KB.")
            .When(c => c.Parameters is { ValueKind: not JsonValueKind.Null });
    }
}

public sealed class SearchPackagesQueryValidator : AbstractValidator<SearchPackagesQuery>
{
    public SearchPackagesQueryValidator()
    {
        RuleFor(q => q.Q).MaximumLength(100);
        RuleFor(q => q.Category).MaximumLength(32);
        RuleFor(q => q.Tag).MaximumLength(24);
        RuleFor(q => q.Language).IsInEnum();
        RuleFor(q => q.Sort).IsInEnum();
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 50);
    }
}

public sealed class ReportPackageRequestValidator : AbstractValidator<ReportPackageRequest>
{
    public ReportPackageRequestValidator()
    {
        RuleFor(r => r.Reason).IsInEnum();
        RuleFor(r => r.Comment).MaximumLength(1000);
    }
}
