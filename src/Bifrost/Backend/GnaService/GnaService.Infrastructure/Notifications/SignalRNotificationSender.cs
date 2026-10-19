using GnaService.Application.Notifications;
using Microsoft.AspNetCore.SignalR;
using Shared.Application.Messaging.Contracts;

namespace GnaService.Infrastructure.Notifications;

public sealed class SignalRNotificationSender<THub>(IHubContext<THub> hub, IConnectionRegistry connections)
    : IRealtimeNotificationSender where THub : Hub
{
    public async Task<bool> TrySendAsync(NotificationDeliveryRequestedMessage notification, CancellationToken cancellationToken)
    {
        if (!connections.IsConnected(notification.UserId)) return false;
        await hub.Clients.User(notification.UserId).SendAsync("NotificationReceived", notification, cancellationToken);
        return true;
    }
}
