using Microsoft.Extensions.Logging;
using Shared.Application.Messaging.Contracts;
using Shared.Domain.Notifications;

namespace GnaService.Application.Notifications;

public sealed class NotificationDispatcher(IRealtimeNotificationSender realtime, IPushNotificationSender push,
    ILogger<NotificationDispatcher> logger)
{
    public async Task DispatchAsync(NotificationDeliveryRequestedMessage notification, CancellationToken cancellationToken)
    {
        string[] channels = notification.DeliveryType == NotificationDeliveryType.RealtimeOnly
            ? ["SignalR"] : ["SignalR", "Push"];
        List<Exception>? failures = null;
        foreach (var channel in channels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var sent = channel == "SignalR"
                    ? await realtime.TrySendAsync(notification, cancellationToken)
                    : await push.TrySendAsync(notification, cancellationToken);
                if (sent)
                {
                    logger.LogDebug("Notification {NotificationId} for {UserId} sent through {Channel}: {Message}",
                        notification.NotificationId, notification.UserId, channel, notification.Message);
                    return;
                }
                logger.LogDebug("Notification {NotificationId}: {Channel} is unavailable for {UserId}.",
                    notification.NotificationId, channel, notification.UserId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
                logger.LogWarning(exception, "Notification {NotificationId}: {Channel} failed for {UserId}.",
                    notification.NotificationId, channel, notification.UserId);
            }
        }
        if (failures is { Count: > 0 })
        {
            // Offline users need no replay, but a failed transport must remain retryable in the outbox.
            throw new AggregateException("Notification delivery failed and no fallback channel succeeded.", failures);
        }
        logger.LogDebug("Notification {NotificationId} stored for {UserId}; no delivery channel is available: {Message}",
            notification.NotificationId, notification.UserId, notification.Message);
    }
}
