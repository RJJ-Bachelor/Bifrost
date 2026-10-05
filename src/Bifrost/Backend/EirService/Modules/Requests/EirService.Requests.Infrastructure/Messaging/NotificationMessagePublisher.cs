using Shared.Application.Messaging;

namespace EirService.Requests.Infrastructure.Messaging;

internal sealed class NotificationMessagePublisher(IMessageBus messageBus)
    : INotificationMessagePublisher
{
    private const string ExchangeName = "notifications";
    private const string NotificationCreatedRoutingKey = "notification.created";

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
            NotificationCreatedRoutingKey,
            cancellationToken);
    }
}
