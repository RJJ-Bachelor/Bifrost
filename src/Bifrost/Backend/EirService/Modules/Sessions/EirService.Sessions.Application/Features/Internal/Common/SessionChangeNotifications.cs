using EirService.Contracts.Participants.GetSessionParticipantIds;
using EirService.Sessions.Application.Repositories;
using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;
using Shared.Domain.Notifications;

namespace EirService.Sessions.Application.Features.Internal.Common;

internal static class SessionChangeNotifications
{
    public static async Task EnqueueAsync(IServiceScopeFactory scopeFactory, Guid sessionId, int generation,
        string change, Action<OutgoingMessage> enqueue, CancellationToken cancellationToken)
    {
        // Event handlers are singletons. Resolve scoped repositories before the lifecycle change commits.
        await using var scope = scopeFactory.CreateAsyncScope();
        var participants = scope.ServiceProvider.GetRequiredService<ICommandHandler<GetSessionParticipantIdsQuery, string[]>>();
        var recipientIds = await participants.ExecuteAsync(new GetSessionParticipantIdsQuery(sessionId), cancellationToken);
        var session = await scope.ServiceProvider.GetRequiredService<ISessionRepository>().GetAsync(sessionId, cancellationToken);
        if (session is not null) recipientIds = [.. recipientIds, session.CreatedByTeacherId];
        foreach (var userId in recipientIds.Distinct(StringComparer.Ordinal))
            enqueue(new OutgoingMessage(Guid.NewGuid(),
                new NotificationCreatedMessage(userId, "Sessionen er opdateret.", sessionId, generation,
                    Change: change, EventType: NotificationEventType.SessionChanged,
                    DeliveryType: NotificationDeliveryType.RealtimeOnly),
                MessageRoutes.Notifications, MessageRoutes.NotificationCreated,
                CoalescingKey: $"session-notification:{sessionId}:{userId}", Priority: 0));
    }
}