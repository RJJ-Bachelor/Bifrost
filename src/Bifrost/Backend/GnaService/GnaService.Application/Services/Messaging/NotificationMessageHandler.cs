using Microsoft.Extensions.Logging;
using Shared.Application.Messaging;
using GnaService.Application.Repositories;
using GnaService.Domain.Entities;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Persistence;
using Shared.Domain.Notifications;

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
        logger.LogDebug(
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
            status: message.Payload.Status,
            eventType: message.Payload.EventType != NotificationEventType.Unknown
                ? message.Payload.EventType : FromLegacyChange(message.Payload.Change),
            deliveryType: message.Payload.DeliveryType);

        if (await notificationRepository.TryAddAsync(notification, cancellationToken))
            await unitOfWork.SaveChangesAsync(cancellationToken);
        else
            logger.LogDebug("Ignored duplicate notification {NotificationId}.", message.Id);
    }

    // Messages already queued before EventType was introduced still carry their original change.
    private static NotificationEventType FromLegacyChange(string? change) => change switch
    {
        "Created" => NotificationEventType.HelpRequestCreated,
        "StudentMessageAdded" => NotificationEventType.StudentMessageAdded,
        "Started" => NotificationEventType.HelpRequestStarted,
        "Answered" => NotificationEventType.HelpRequestAnswered,
        "FaqCreated" => NotificationEventType.FaqCreated,
        "FaqSuggested" => NotificationEventType.FaqAnswered,
        _ => NotificationEventType.Unknown
    };
}
