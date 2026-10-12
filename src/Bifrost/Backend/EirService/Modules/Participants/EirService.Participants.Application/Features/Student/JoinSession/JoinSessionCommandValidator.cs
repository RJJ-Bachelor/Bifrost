using FluentValidation;

namespace EirService.Participants.Application.Features.Student.JoinSession;

public sealed class JoinSessionCommandValidator : AbstractValidator<JoinSessionCommand>
{
    public JoinSessionCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty();
        RuleFor(command => command.StudentId).NotEmpty();
    }
}
