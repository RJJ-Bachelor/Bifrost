using GnaService.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GnaService.Api.Hubs;

[Authorize]
public sealed class NotificationHub(IConnectionRegistry connections, ILogger<NotificationHub> logger) : Hub
{
    public override Task OnConnectedAsync()
    {
        if (string.IsNullOrWhiteSpace(Context.UserIdentifier))
            throw new HubException("An authenticated user ID is required.");
        connections.Add(Context.UserIdentifier, Context.ConnectionId);
        logger.LogInformation("Notification connection {ConnectionId} opened for {UserId}.", Context.ConnectionId, Context.UserIdentifier);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        logger.LogInformation("Notification connection {ConnectionId} closed for {UserId}.", Context.ConnectionId, Context.UserIdentifier);
        return base.OnDisconnectedAsync(exception);
    }
}
