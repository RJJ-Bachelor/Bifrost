using FluentValidation;

namespace EirService.Sessions.Application.Features.CreateSession;

public sealed class CreateSessionCommandValidator : AbstractValidator<CreateSessionCommand>
{
    public CreateSessionCommandValidator()
    {
        RuleFor(x => x.TeacherId)
            .NotEmpty()
            .WithMessage(nameof(CreateSessionCommand.TeacherId));
    }
}
