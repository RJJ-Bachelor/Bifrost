using Microsoft.Extensions.Logging;
using Shared.Application.Messaging.Contracts;

namespace GnaService.Application.Notifications;

public sealed class NotificationDispatcher(IRealtimeNotificationSender realtime, IPushNotificationSender push,
    ILogger<NotificationDispatcher> logger)
{
    public async Task DispatchAsync(NotificationDeliveryRequestedMessage notification, CancellationToken cancellationToken)
    {
        foreach (var channel in new[] { "SignalR", "Push" })
        {
            try
            {
                var sent = channel == "SignalR"
                    ? await realtime.TrySendAsync(notification, cancellationToken)
                    : await push.TrySendAsync(notification, cancellationToken);
                if (sent)
                {
                    logger.LogInformation("Notification {NotificationId} for {UserId} sent through {Channel}: {Message}",
                        notification.NotificationId, notification.UserId, channel, notification.Message);
                    return;
                }
                logger.LogInformation("Notification {NotificationId}: {Channel} is unavailable for {UserId}.",
                    notification.NotificationId, channel, notification.UserId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Notification {NotificationId}: {Channel} failed for {UserId}.",
                    notification.NotificationId, channel, notification.UserId);
            }
        }
        logger.LogInformation("Notification {NotificationId} stored for {UserId}; no delivery channel is available: {Message}",
            notification.NotificationId, notification.UserId, notification.Message);
    }
}
