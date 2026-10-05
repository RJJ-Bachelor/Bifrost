using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;

namespace GnaService.Application.Messaging;

public sealed class NotificationMessageHandler(
    ILogger<NotificationMessageHandler> logger) : INotificationMessageHandler
{
    public Task HandleAsync(
        MessageEnvelope<NotificationCreatedMessage> message,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Gna received notification {MessageId} for user {UserId}: {Messages}",
            message.Id,
            message.Payload.UserId,
            message.Payload.Messages);

        return Task.CompletedTask;
    }
}
