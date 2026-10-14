using Microsoft.AspNetCore.SignalR;

namespace GnaService.Api.Hubs;

public sealed class NotificationUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User.Identity?.IsAuthenticated == true ? connection.User.FindFirst("sub")?.Value : null;
}
