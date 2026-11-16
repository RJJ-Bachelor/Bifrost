using FluentValidation;
using GnaService.Application.Features.Common.RegisterPushSubscription;

namespace GnaService.Application.Features.Common.UnregisterPushSubscription;

public sealed class UnregisterPushSubscriptionCommandValidator : AbstractValidator<UnregisterPushSubscriptionCommand>
{
    public UnregisterPushSubscriptionCommandValidator()
    {
        RuleFor(command => command.UserId).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Endpoint).Must(RegisterPushSubscriptionCommandValidator.IsHttpsEndpoint)
            .WithMessage("An HTTPS endpoint from a supported push service is required.");
    }
}
