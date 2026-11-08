using GnaService.Application.Notifications;
using Microsoft.AspNetCore.SignalR;
using Shared.Application.Messaging.Contracts;
using Shared.Domain.Notifications;

namespace GnaService.Infrastructure.Notifications;

public sealed class SignalRNotificationSender<THub>(IHubContext<THub> hub, IConnectionRegistry connections)
    : IRealtimeNotificationSender, IRealtimeSessionChangeSender where THub : Hub
{
    public async Task<bool> TrySendAsync(NotificationDeliveryRequestedMessage notification, CancellationToken cancellationToken)
    {
        if (!connections.IsConnected(notification.UserId)) return false;
        await hub.Clients.User(notification.UserId).SendAsync("NotificationReceived", notification, cancellationToken);
        return true;
    }

    public Task SendAsync(Guid notificationId, SessionChangedMessage message, CancellationToken cancellationToken)
    {
        var notification = new NotificationDeliveryRequestedMessage(notificationId, "", "Sessionen er opdateret.",
            DateTimeOffset.UtcNow, message.SessionId, Generation: message.Generation, Change: message.Change,
            EventType: NotificationEventType.SessionChanged, DeliveryType: NotificationDeliveryType.RealtimeOnly);
        // SignalR targets authenticated user IDs across all their tabs; offline users need no replay.
        return hub.Clients.Users(message.UserIds.Distinct(StringComparer.Ordinal).ToArray())
            .SendAsync("NotificationReceived", notification, cancellationToken);
    }
}
