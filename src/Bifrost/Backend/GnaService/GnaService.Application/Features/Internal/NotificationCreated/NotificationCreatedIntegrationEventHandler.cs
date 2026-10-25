using FastEndpoints;
using GnaService.Domain.DomainEvents;
using Shared.Application.DomainEvents;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

namespace GnaService.Application.Features.Internal.NotificationCreated;

public sealed class NotificationCreatedIntegrationEventHandler
    : IEventHandler<DomainEventNotification<NotificationCreatedDomainEvent>>
{
    public Task HandleAsync(DomainEventNotification<NotificationCreatedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var created = notification.DomainEvent.Notification;
        notification.EnqueueIntegrationEvent(new OutgoingMessage(created.Id,
            new NotificationDeliveryRequestedMessage(created.Id, created.UserId.Value, created.Messages.Value, created.CreatedAt,
                created.SessionId, created.HelpRequestId, created.Generation, created.Change, created.Status, created.EventType, created.DeliveryType),
            MessageRoutes.Notifications, MessageRoutes.NotificationDeliveryRequested));
        return Task.CompletedTask;
    }
}
