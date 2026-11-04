using EirService.Contracts.HelpRequests.HandleSessionChange;
using EirService.Contracts.Messaging;
using EirService.Sessions.Application.Features.Internal.Common;
using Microsoft.Extensions.DependencyInjection;
using EirService.Sessions.Domain.DomainEvents;
using FastEndpoints;
using Shared.Application.DomainEvents;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

namespace EirService.Sessions.Application.Features.Internal.SessionArchived;

public sealed class SessionArchivedIntegrationEventUpgradeHandler(IServiceScopeFactory scopeFactory)
    : IEventHandler<DomainEventNotification<SessionArchivedDomainEvent>>
{
    public async Task HandleAsync(DomainEventNotification<SessionArchivedDomainEvent> notification, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var domainEvent = notification.DomainEvent;
        var integrationEvent = new SessionUpdatedIntegrationEvent(domainEvent.Id, domainEvent.SessionId,
            domainEvent.CurrentGeneration, SessionUpdateType.Archived, domainEvent.ArchivedAtUtc, domainEvent.PreviousGeneration);
        notification.EnqueueIntegrationEvent(new OutgoingMessage(domainEvent.Id, integrationEvent,
            MessageRoutes.Sessions, MessageRoutes.SessionUpdated));
        notification.EnqueueInternalCommand(domainEvent.Id,
            new HandleSessionChangeCommand(domainEvent.Id, domainEvent.SessionId, domainEvent.CurrentGeneration,
                SessionChangeType.Archived, domainEvent.ArchivedAtUtc, domainEvent.PreviousGeneration), EirModuleRoutes.SessionChanged);
        await SessionChangeNotifications.EnqueueAsync(scopeFactory, domainEvent.SessionId, domainEvent.CurrentGeneration,
            "Archived", notification.EnqueueIntegrationEvent, cancellationToken);
    }
}
