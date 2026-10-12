using EirService.Sessions.Domain.DomainEvents;
using FastEndpoints;
using Shared.Application.DomainEvents;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

namespace EirService.Sessions.Application.Features.Internal.SessionCleared;

public sealed class SessionClearedIntegrationEventUpgradeHandler
    : IEventHandler<DomainEventNotification<SessionClearedDomainEvent>>
{
    public Task HandleAsync(DomainEventNotification<SessionClearedDomainEvent> notification, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var domainEvent = notification.DomainEvent;
        var integrationEvent = new SessionUpdatedIntegrationEvent(domainEvent.Id, domainEvent.SessionId,
            domainEvent.CurrentGeneration, SessionUpdateType.Cleared, domainEvent.ClearedAtUtc, domainEvent.PreviousGeneration);
        notification.EnqueueIntegrationEvent(new OutgoingMessage(domainEvent.Id, integrationEvent,
            MessageRoutes.Sessions, MessageRoutes.SessionUpdated));
        return Task.CompletedTask;
    }
}
