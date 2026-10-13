using EirService.HelpRequests.Domain.ValueObjects;
using FluentValidation;

namespace EirService.HelpRequests.Application.Features.Student.CreateHelpRequest;

public sealed class CreateHelpRequestCommandValidator : AbstractValidator<CreateHelpRequestCommand>
{
    public CreateHelpRequestCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.StudentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Message).NotEmpty().MaximumLength(MessageText.MaxLength);
        RuleFor(x => x.HelpType).NotNull().IsInEnum();
    }
}
