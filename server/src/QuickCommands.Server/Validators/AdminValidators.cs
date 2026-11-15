using FluentValidation;
using QuickCommands.Server.Dtos.Requests;

namespace QuickCommands.Server.Validators;

public sealed class ReportListQueryValidator : AbstractValidator<ReportListQuery>
{
    public ReportListQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 50);
    }
}

public sealed class RemovePackageRequestValidator : AbstractValidator<RemovePackageRequest>
{
    public RemovePackageRequestValidator() => RuleFor(r => r.Reason).IsInEnum();
}

public sealed class BanUserRequestValidator : AbstractValidator<BanUserRequest>
{
    public BanUserRequestValidator()
    {
        RuleFor(r => r.Reason).MaximumLength(500);
        RuleFor(r => r.RemovePackagesWithReason).IsInEnum();
    }
}
