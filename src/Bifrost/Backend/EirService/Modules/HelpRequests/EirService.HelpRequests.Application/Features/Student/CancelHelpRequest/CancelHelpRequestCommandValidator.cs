using FluentValidation;

namespace EirService.HelpRequests.Application.Features.Student.CancelHelpRequest;

public sealed class CancelHelpRequestCommandValidator : AbstractValidator<CancelHelpRequestCommand>
{
    public CancelHelpRequestCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.StudentId).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Action).Equal("Cancel").WithMessage("Students can only cancel requests.");
    }
}
