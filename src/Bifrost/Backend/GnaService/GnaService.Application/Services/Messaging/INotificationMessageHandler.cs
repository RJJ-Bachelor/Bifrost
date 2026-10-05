using Shared.Application.Messaging;

namespace GnaService.Application.Services.Messaging;

public interface INotificationMessageHandler
{
    Task HandleAsync(
        MessageEnvelope<NotificationCreatedMessage> message,
        CancellationToken cancellationToken);
}
