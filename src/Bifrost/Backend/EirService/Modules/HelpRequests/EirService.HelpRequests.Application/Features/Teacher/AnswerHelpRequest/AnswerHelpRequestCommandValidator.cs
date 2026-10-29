using EirService.HelpRequests.Domain.ValueObjects;
using FluentValidation;

namespace EirService.HelpRequests.Application.Features.Teacher.AnswerHelpRequest;

public sealed class AnswerHelpRequestCommandValidator : AbstractValidator<AnswerHelpRequestCommand>
{
    public AnswerHelpRequestCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.TeacherId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(MessageText.MaxLength);
        RuleFor(x => x.Question).NotEmpty().MaximumLength(MessageText.MaxLength).When(x => x.Question is not null);
    }
}
