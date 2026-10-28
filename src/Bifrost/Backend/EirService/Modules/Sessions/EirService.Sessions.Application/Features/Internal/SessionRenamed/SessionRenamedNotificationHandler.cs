using EirService.Sessions.Application.Features.Internal.Common;
using EirService.Sessions.Domain.DomainEvents;
using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.DomainEvents;

namespace EirService.Sessions.Application.Features.Internal.SessionRenamed;

public sealed class SessionRenamedNotificationHandler(IServiceScopeFactory scopeFactory)
    : IEventHandler<DomainEventNotification<SessionRenamedDomainEvent>>
{
    public async Task HandleAsync(DomainEventNotification<SessionRenamedDomainEvent> notification, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var domainEvent = notification.DomainEvent;
        await SessionChangeNotifications.EnqueueAsync(scopeFactory, domainEvent.SessionId, domainEvent.Generation,
            "Renamed", notification.EnqueueIntegrationEvent, cancellationToken);
    }
}
