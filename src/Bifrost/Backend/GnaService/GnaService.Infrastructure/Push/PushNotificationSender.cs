using GnaService.Application.Notifications;
using GnaService.Application.Repositories;
using Microsoft.Extensions.Logging;
using Shared.Application.Messaging.Contracts;

namespace GnaService.Infrastructure.Push;

public sealed class PushNotificationSender(IPushSubscriptionRepository subscriptions,
    ILogger<PushNotificationSender> logger) : IPushNotificationSender
{
    public async Task<bool> TrySendAsync(NotificationDeliveryRequestedMessage notification, CancellationToken cancellationToken)
    {
        var saved = await subscriptions.GetForUserAsync(notification.UserId, cancellationToken);
        if (saved.Count > 0)
            logger.LogInformation("Push is not implemented. Notification {NotificationId} has {SubscriptionCount} saved subscriptions.",
                notification.NotificationId, saved.Count);
        return false;
    }
}
