using FastEndpoints;
using GnaService.Application.Repositories;
using GnaService.Domain.Entities;

namespace GnaService.Application.Features.Common.RegisterPushSubscription;

public sealed class RegisterPushSubscriptionCommandHandler(IPushSubscriptionRepository subscriptions)
    : ICommandHandler<RegisterPushSubscriptionCommand>
{
    public Task ExecuteAsync(RegisterPushSubscriptionCommand command, CancellationToken cancellationToken) =>
        subscriptions.SaveAsync(PushSubscription.Create(command.UserId, command.Endpoint, command.P256dh, command.Auth), cancellationToken);
}
