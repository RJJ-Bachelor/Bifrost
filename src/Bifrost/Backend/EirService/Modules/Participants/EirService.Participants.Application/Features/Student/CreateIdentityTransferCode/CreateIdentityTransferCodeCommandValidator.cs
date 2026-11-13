using FluentValidation;

namespace EirService.Participants.Application.Features.Student.CreateIdentityTransferCode;

public sealed class CreateIdentityTransferCodeCommandValidator : AbstractValidator<CreateIdentityTransferCodeCommand>
{
    public CreateIdentityTransferCodeCommandValidator()
    {
        RuleFor(command => command.StudentId).NotEmpty().Must(id => Guid.TryParseExact(id, "N", out _));
    }
}
