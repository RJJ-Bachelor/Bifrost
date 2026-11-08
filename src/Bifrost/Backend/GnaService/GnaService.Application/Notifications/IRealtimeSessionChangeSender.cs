using Shared.Application.Messaging.Contracts;

namespace GnaService.Application.Notifications;

public interface IRealtimeSessionChangeSender
{
    Task SendAsync(Guid notificationId, SessionChangedMessage message, CancellationToken cancellationToken);
}
