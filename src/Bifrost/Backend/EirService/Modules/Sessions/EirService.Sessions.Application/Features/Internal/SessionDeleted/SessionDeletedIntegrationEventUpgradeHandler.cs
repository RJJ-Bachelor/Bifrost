using EirService.Sessions.Domain.DomainEvents;
using FastEndpoints;
using Shared.Application.DomainEvents;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

namespace EirService.Sessions.Application.Features.Internal.SessionDeleted;

public sealed class SessionDeletedIntegrationEventUpgradeHandler
    : IEventHandler<DomainEventNotification<SessionDeletedDomainEvent>>
{
    public Task HandleAsync(DomainEventNotification<SessionDeletedDomainEvent> notification, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var domainEvent = notification.DomainEvent;
        var integrationEvent = new SessionUpdatedIntegrationEvent(domainEvent.Id, domainEvent.SessionId,
            domainEvent.Generation, SessionUpdateType.Deleted, domainEvent.DeletedAtUtc);
        notification.EnqueueIntegrationEvent(new OutgoingMessage(domainEvent.Id, integrationEvent,
            MessageRoutes.Sessions, MessageRoutes.SessionUpdated));
        return Task.CompletedTask;
    }
}
