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
        var integrationEvent = new SessionClearedIntegrationEvent(domainEvent.Id, domainEvent.SessionId,
            domainEvent.PreviousGeneration, domainEvent.CurrentGeneration, domainEvent.ClearedAtUtc);
        notification.EnqueueIntegrationEvent(new OutgoingMessage(domainEvent.Id.ToString(), integrationEvent,
            MessageRoutes.Sessions, MessageRoutes.SessionCleared));
        return Task.CompletedTask;
    }
}
