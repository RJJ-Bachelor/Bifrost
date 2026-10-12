using FastEndpoints;
using Shared.Application.Messaging.Outbox;
using Shared.Domain.DomainEvents;

namespace Shared.Application.DomainEvents;

// Adapts a pure domain event to FastEndpoints and stages integration events for the same commit.
public sealed class DomainEventNotification<TDomainEvent>(
    TDomainEvent domainEvent, Action<OutgoingMessage> enqueueIntegrationEvent) : IEvent where TDomainEvent : IDomainEvent
{
    public TDomainEvent DomainEvent { get; } = domainEvent;

    public void EnqueueIntegrationEvent(OutgoingMessage message) => enqueueIntegrationEvent(message);
}
