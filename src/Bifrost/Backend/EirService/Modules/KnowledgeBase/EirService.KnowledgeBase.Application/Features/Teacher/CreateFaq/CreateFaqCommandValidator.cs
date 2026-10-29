using EirService.KnowledgeBase.Domain.Entities;
using FluentValidation;

namespace EirService.KnowledgeBase.Application.Features.Teacher.CreateFaq;

public sealed class CreateFaqCommandValidator : AbstractValidator<CreateFaqCommand>
{
    public CreateFaqCommandValidator()
    {
        RuleFor(command => command.SessionId).NotEmpty();
        RuleFor(command => command.TeacherId).NotEmpty();
        RuleFor(command => command.Question)
            .Must(text => !string.IsNullOrWhiteSpace(text) && text.Trim().Length <= FaqAnswer.MaxTextLength)
            .WithMessage("Question must contain 1-4000 characters after trimming.");
        RuleFor(command => command.Answer)
            .Must(text => !string.IsNullOrWhiteSpace(text) && text.Trim().Length <= FaqAnswer.MaxTextLength)
            .WithMessage("Answer must contain 1-4000 characters after trimming.");
    }
}
