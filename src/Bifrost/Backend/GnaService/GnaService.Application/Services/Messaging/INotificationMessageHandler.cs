using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;

namespace GnaService.Application.Services.Messaging;

public interface INotificationMessageHandler
{
    Task HandleAsync(
        MessageEnvelope<NotificationCreatedMessage> message,
        CancellationToken cancellationToken);
}
