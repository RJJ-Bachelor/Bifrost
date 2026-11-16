using FastEndpoints;
using GnaService.Application.Repositories;

namespace GnaService.Application.Features.Common.UnregisterPushSubscription;

public sealed class UnregisterPushSubscriptionCommandHandler(IPushSubscriptionRepository subscriptions)
    : ICommandHandler<UnregisterPushSubscriptionCommand>
{
    public Task ExecuteAsync(UnregisterPushSubscriptionCommand command, CancellationToken cancellationToken) =>
        subscriptions.RemoveAsync(command.UserId, command.Endpoint, cancellationToken);
}
