using EirService.Sessions.Application.Features.Internal.Common;
using Microsoft.Extensions.DependencyInjection;
using EirService.Sessions.Domain.DomainEvents;
using FastEndpoints;
using Shared.Application.DomainEvents;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

namespace EirService.Sessions.Application.Features.Internal.SessionDeleted;

public sealed class SessionDeletedIntegrationEventUpgradeHandler(IServiceScopeFactory scopeFactory)
    : IEventHandler<DomainEventNotification<SessionDeletedDomainEvent>>
{
    public async Task HandleAsync(DomainEventNotification<SessionDeletedDomainEvent> notification, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var domainEvent = notification.DomainEvent;
        var integrationEvent = new SessionUpdatedIntegrationEvent(domainEvent.Id, domainEvent.SessionId,
            domainEvent.Generation, SessionUpdateType.Deleted, domainEvent.DeletedAtUtc);
        notification.EnqueueIntegrationEvent(new OutgoingMessage(domainEvent.Id, integrationEvent,
            MessageRoutes.Sessions, MessageRoutes.SessionUpdated));
        await SessionChangeNotifications.EnqueueAsync(scopeFactory, domainEvent.SessionId, domainEvent.Generation,
            "Deleted", notification.EnqueueIntegrationEvent, cancellationToken);
    }
}
