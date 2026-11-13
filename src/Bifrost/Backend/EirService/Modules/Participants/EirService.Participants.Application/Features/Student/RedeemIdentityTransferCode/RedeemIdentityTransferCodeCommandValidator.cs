using FluentValidation;
using Shared.Domain.ValueObjects;

namespace EirService.Participants.Application.Features.Student.RedeemIdentityTransferCode;

public sealed class RedeemIdentityTransferCodeCommandValidator : AbstractValidator<RedeemIdentityTransferCodeCommand>
{
    public RedeemIdentityTransferCodeCommandValidator()
    {
        RuleFor(command => command.Code).Must(ShortCode.IsValid)
            .WithMessage("A valid eight-character transfer code is required.");
    }
}
