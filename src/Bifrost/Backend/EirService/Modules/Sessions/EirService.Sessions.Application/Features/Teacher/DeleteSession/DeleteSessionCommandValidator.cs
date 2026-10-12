using FluentValidation;

namespace EirService.Sessions.Application.Features.Teacher.DeleteSession;

public sealed class DeleteSessionCommandValidator : AbstractValidator<DeleteSessionCommand>
{
    public DeleteSessionCommandValidator()
    {
        RuleFor(command => command.SessionId).NotEmpty();
        RuleFor(command => command.TeacherId).NotEmpty();
    }
}
