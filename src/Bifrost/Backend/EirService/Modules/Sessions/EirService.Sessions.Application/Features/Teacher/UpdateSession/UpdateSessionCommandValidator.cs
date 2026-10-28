using FluentValidation;

namespace EirService.Sessions.Application.Features.Teacher.UpdateSession;

public sealed class UpdateSessionCommandValidator : AbstractValidator<UpdateSessionCommand>
{
    public UpdateSessionCommandValidator()
    {
        RuleFor(command => command.SessionId).NotEmpty();
        RuleFor(command => command.TeacherId).NotEmpty();
        RuleFor(command => command.Title)
            .Must(title => !string.IsNullOrWhiteSpace(title) && title.Trim().Length <= 200)
            .WithMessage("Title must contain 1–200 characters after trimming.");
    }
}
