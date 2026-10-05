using Shared.Application.Messaging;

namespace GnaService.Application.Messaging;

public interface INotificationMessageHandler
{
    Task HandleAsync(
        MessageEnvelope<NotificationCreatedMessage> message,
        CancellationToken cancellationToken);
}
