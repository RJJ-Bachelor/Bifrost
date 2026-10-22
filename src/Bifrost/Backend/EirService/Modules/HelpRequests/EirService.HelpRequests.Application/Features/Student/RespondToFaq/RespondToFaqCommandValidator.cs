using FluentValidation;

namespace EirService.HelpRequests.Application.Features.Student.RespondToFaq;

public sealed class RespondToFaqCommandValidator : AbstractValidator<RespondToFaqCommand>
{
    public RespondToFaqCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.StudentId).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Accept).NotNull();
    }
}
