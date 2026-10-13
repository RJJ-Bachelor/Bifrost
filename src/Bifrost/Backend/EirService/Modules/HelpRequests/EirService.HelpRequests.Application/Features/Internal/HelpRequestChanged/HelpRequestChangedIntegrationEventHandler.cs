using EirService.HelpRequests.Domain.DomainEvents;
using FastEndpoints;
using Shared.Application.DomainEvents;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

namespace EirService.HelpRequests.Application.Features.Internal.HelpRequestChanged;

public sealed class HelpRequestChangedIntegrationEventHandler
    : IEventHandler<DomainEventNotification<HelpRequestChangedDomainEvent>>
{
    public Task HandleAsync(DomainEventNotification<HelpRequestChangedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var change = notification.DomainEvent;
        var request = change.Request;
        notification.EnqueueIntegrationEvent(new OutgoingMessage(change.Id,
            new HelpRequestUpdatedIntegrationEvent(change.Id, request.Id, request.SessionId, request.Generation,
                request.StudentId, change.Change, request.Status.ToString(), request.HelpType.ToString(),
                request.AssignedTeacherId, request.QueuedAt, change.OccurredAt),
            MessageRoutes.Requests, MessageRoutes.HelpRequestUpdated));
        if (change.Change == "Created")
        {
            var text = request.Messages.OrderBy(message => message.SequenceNumber).First().Text.Value;
            notification.EnqueueIntegrationEvent(new OutgoingMessage(request.Id,
                new HelpRequestCreatedMessage(text, request.SessionId, request.Generation),
                MessageRoutes.Requests, MessageRoutes.HelpRequestCreated));
            notification.EnqueueIntegrationEvent(new OutgoingMessage(request.Id,
                new NotificationCreatedMessage(request.StudentId, text, request.SessionId, request.Generation),
                MessageRoutes.Notifications, MessageRoutes.NotificationCreated));
        }
        if (change.Change == "Answered")
        {
            var messages = request.Messages.OrderBy(message => message.SequenceNumber).ToArray();
            notification.EnqueueIntegrationEvent(new OutgoingMessage(change.Id,
                new HelpRequestAnsweredMessage(request.Id, request.SessionId, request.Generation,
                    messages[^2].Text.Value, messages[^1].Text.Value),
                MessageRoutes.Requests, MessageRoutes.HelpRequestAnswered));
        }
        return Task.CompletedTask;
    }
}
