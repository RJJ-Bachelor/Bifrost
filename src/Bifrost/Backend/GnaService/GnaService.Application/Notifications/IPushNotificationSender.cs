using Shared.Application.Messaging.Contracts;

namespace GnaService.Application.Notifications;

public interface IPushNotificationSender
{
    Task<bool> TrySendAsync(NotificationDeliveryRequestedMessage notification, CancellationToken cancellationToken);
}
