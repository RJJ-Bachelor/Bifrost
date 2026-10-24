using FluentValidation;

namespace EirService.Sessions.Application.Features.Teacher.ArchiveSession;

public sealed class ArchiveSessionCommandValidator : AbstractValidator<ArchiveSessionCommand>
{
    public ArchiveSessionCommandValidator()
    {
        RuleFor(command => command.SessionId).NotEmpty();
        RuleFor(command => command.TeacherId).NotEmpty();
        RuleFor(command => command.ExpectedGeneration).InclusiveBetween(1, int.MaxValue - 1);
    }
}
