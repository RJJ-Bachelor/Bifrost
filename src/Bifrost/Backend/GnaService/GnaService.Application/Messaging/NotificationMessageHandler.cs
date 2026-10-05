using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using GnaService.Application.Repositories;
using GnaService.Domain.Entities;

namespace GnaService.Application.Messaging;

public sealed class NotificationMessageHandler(
    ILogger<NotificationMessageHandler> logger,
    INotificationRepository notificationRepository) : INotificationMessageHandler
{
    public async Task HandleAsync(
        MessageEnvelope<NotificationCreatedMessage> message,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Gna received notification {MessageId} for user {UserId}: {Messages}",
            message.Id,
            message.Payload.UserId,
            message.Payload.Messages);

        var notification = Notification.Create(
            message.Id,
            message.Payload.UserId,
            message.Payload.Messages);

        await notificationRepository.AddAsync(notification, cancellationToken);
    }
}
