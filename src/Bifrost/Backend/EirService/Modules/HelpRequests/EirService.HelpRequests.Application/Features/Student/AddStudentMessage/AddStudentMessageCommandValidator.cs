using EirService.HelpRequests.Domain.ValueObjects;
using FluentValidation;

namespace EirService.HelpRequests.Application.Features.Student.AddStudentMessage;

public sealed class AddStudentMessageCommandValidator : AbstractValidator<AddStudentMessageCommand>
{
    public AddStudentMessageCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.StudentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(MessageText.MaxLength);
    }
}
