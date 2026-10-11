using FluentValidation;

namespace EirService.Sessions.Application.Features.ClearSession;

public sealed class ClearSessionCommandValidator : AbstractValidator<ClearSessionCommand>
{
    public ClearSessionCommandValidator()
    {
        RuleFor(command => command.SessionId).NotEmpty();
        RuleFor(command => command.TeacherId).NotEmpty();
        RuleFor(command => command.ExpectedGeneration).InclusiveBetween(1, int.MaxValue - 1);
    }
}
