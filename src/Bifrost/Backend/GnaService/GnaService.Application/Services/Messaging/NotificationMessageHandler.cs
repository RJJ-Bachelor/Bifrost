using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using GnaService.Application.Repositories;
using GnaService.Domain.Entities;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Persistence;

namespace GnaService.Application.Services.Messaging;

public sealed class NotificationMessageHandler(
    ILogger<NotificationMessageHandler> logger,
    INotificationRepository notificationRepository,
    IUnitOfWork<INotificationRepository> unitOfWork,
    TimeProvider timeProvider) : INotificationMessageHandler
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
            message.Payload.Messages,
            now: timeProvider.GetUtcNow(),
            sessionId: message.Payload.SessionId,
            generation: message.Payload.Generation,
            helpRequestId: message.Payload.HelpRequestId,
            change: message.Payload.Change,
            status: message.Payload.Status);

        if (await notificationRepository.TryAddAsync(notification, cancellationToken))
            await unitOfWork.SaveChangesAsync(cancellationToken);
        else
            logger.LogInformation("Ignored duplicate notification {NotificationId}.", message.Id);
    }
}
