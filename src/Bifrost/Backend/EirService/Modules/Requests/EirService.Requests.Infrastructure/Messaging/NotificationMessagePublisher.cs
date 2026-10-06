using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace EirService.Requests.Infrastructure.Messaging;

internal sealed class NotificationMessagePublisher(IMessageBus messageBus)
    : INotificationMessagePublisher
{
    private const string ExchangeName = "notifications";
    private const string NotificationRequestedRoutingKey = "gna-notification-requested";

    public Task PublishNotificationCreatedAsync(
        string id,
        string userId,
        string messages,
        CancellationToken cancellationToken = default)
    {
        return messageBus.PublishAsync(
            id,
            new NotificationCreatedMessage(userId, messages),
            ExchangeName,
            NotificationRequestedRoutingKey,
            cancellationToken);
    }
}
