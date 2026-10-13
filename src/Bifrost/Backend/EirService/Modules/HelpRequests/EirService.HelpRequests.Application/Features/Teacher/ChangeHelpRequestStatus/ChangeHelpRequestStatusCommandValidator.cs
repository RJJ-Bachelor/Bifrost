using FluentValidation;

namespace EirService.HelpRequests.Application.Features.Teacher.ChangeHelpRequestStatus;

public sealed class ChangeHelpRequestStatusCommandValidator : AbstractValidator<ChangeHelpRequestStatusCommand>
{
    public ChangeHelpRequestStatusCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.TeacherId).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Action).NotNull().IsInEnum();
    }
}
