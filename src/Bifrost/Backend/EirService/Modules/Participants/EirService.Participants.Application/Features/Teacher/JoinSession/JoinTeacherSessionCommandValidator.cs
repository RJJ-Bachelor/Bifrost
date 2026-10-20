using FluentValidation;

namespace EirService.Participants.Application.Features.Teacher.JoinSession;

public sealed class JoinTeacherSessionCommandValidator : AbstractValidator<JoinTeacherSessionCommand>
{
    public JoinTeacherSessionCommandValidator()
    {
        RuleFor(command => command.Code).NotEmpty();
        RuleFor(command => command.TeacherId).NotEmpty().MaximumLength(100);
    }
}
