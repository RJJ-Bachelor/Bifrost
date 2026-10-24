using EirService.Sessions.Domain.DomainEvents;
using FastEndpoints;
using Shared.Application.DomainEvents;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

namespace EirService.Sessions.Application.Features.Internal.SessionArchived;

public sealed class SessionArchivedIntegrationEventUpgradeHandler
    : IEventHandler<DomainEventNotification<SessionArchivedDomainEvent>>
{
    public Task HandleAsync(DomainEventNotification<SessionArchivedDomainEvent> notification, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var domainEvent = notification.DomainEvent;
        var integrationEvent = new SessionUpdatedIntegrationEvent(domainEvent.Id, domainEvent.SessionId,
            domainEvent.CurrentGeneration, SessionUpdateType.Archived, domainEvent.ArchivedAtUtc, domainEvent.PreviousGeneration);
        notification.EnqueueIntegrationEvent(new OutgoingMessage(domainEvent.Id, integrationEvent,
            MessageRoutes.Sessions, MessageRoutes.SessionUpdated));
        return Task.CompletedTask;
    }
}
