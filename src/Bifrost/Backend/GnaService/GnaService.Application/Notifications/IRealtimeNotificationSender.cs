using Shared.Application.Messaging.Contracts;

namespace GnaService.Application.Notifications;

public interface IRealtimeNotificationSender
{
    Task<bool> TrySendAsync(NotificationDeliveryRequestedMessage notification, CancellationToken cancellationToken);
}
