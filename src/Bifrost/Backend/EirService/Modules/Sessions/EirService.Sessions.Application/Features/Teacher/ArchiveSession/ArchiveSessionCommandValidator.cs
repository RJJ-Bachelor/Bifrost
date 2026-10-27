using FluentValidation;

namespace EirService.Sessions.Application.Features.Teacher.ArchiveSession;

public sealed class ArchiveSessionCommandValidator : AbstractValidator<ArchiveSessionCommand>
{
    public ArchiveSessionCommandValidator()
    {
        RuleFor(command => command.SessionId).NotEmpty();
        RuleFor(command => command.TeacherId).NotEmpty();
        RuleFor(command => command.ExpectedGeneration).InclusiveBetween(1, int.MaxValue - 1);
        RuleFor(command => command.Title)
            .Must(title => title is null || (!string.IsNullOrWhiteSpace(title) && title.Trim().Length <= 200))
            .WithMessage("Title must contain 1-200 characters after trimming when provided.");
    }
}
