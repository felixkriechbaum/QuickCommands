using FluentValidation;
using QuickCommands.Server.Dtos.Requests;

namespace QuickCommands.Server.Validators;

public sealed class UpdateMeRequestValidator : AbstractValidator<UpdateMeRequest>
{
    public UpdateMeRequestValidator()
    {
        RuleFor(r => r.Handle)
            .Must(h => StoreRules.IsValidHandle(StoreRules.NormalizeHandle(h)))
            .WithMessage("The handle must be 3–20 characters: a-z, 0-9, '_' or '-'.");
    }
}
