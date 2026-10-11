using FluentValidation;

namespace EirService.Sessions.Application.Features.Teacher.CreateSession;

public sealed class CreateSessionCommandValidator : AbstractValidator<CreateSessionCommand>
{
    public CreateSessionCommandValidator()
    {
        RuleFor(x => x.TeacherId)
            .NotEmpty()
            .WithMessage(nameof(CreateSessionCommand.TeacherId));

        RuleFor(x => x.Title)
            .Must(title => !string.IsNullOrWhiteSpace(title) && title.Trim().Length <= 200)
            .WithMessage("Title must contain 1–200 characters after trimming.");
    }
}
