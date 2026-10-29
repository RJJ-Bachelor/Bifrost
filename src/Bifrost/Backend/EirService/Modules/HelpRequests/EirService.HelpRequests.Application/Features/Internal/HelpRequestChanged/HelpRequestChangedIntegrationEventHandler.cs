using EirService.Contracts.Participants.GetSessionParticipantIds;
using EirService.Contracts.Sessions.GetHelpRequestSession;
using EirService.HelpRequests.Application.Repositories;
using EirService.HelpRequests.Domain.DomainEvents;
using EirService.HelpRequests.Domain.Entities;
using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.DomainEvents;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;
using Shared.Domain.Notifications;

namespace EirService.HelpRequests.Application.Features.Internal.HelpRequestChanged;

public sealed class HelpRequestChangedIntegrationEventHandler(IServiceScopeFactory scopeFactory)
    : IEventHandler<DomainEventNotification<HelpRequestChangedDomainEvent>>
{
    public async Task HandleAsync(DomainEventNotification<HelpRequestChangedDomainEvent> notification, CancellationToken cancellationToken)
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
            // Both help types contribute their original question. Follow-ups only update the queue.
            var text = request.Messages.Single(message => message.MessageType == HelpRequestMessageType.StudentQuestion).Text.Value;
            notification.EnqueueIntegrationEvent(new OutgoingMessage(request.Id,
                new HelpRequestCreatedMessage(request.Id, request.SessionId, request.Generation, text),
                MessageRoutes.Requests, MessageRoutes.HelpRequestCreated));
        }

        if (change.Change == "Answered")
        {
            var messages = request.Messages.OrderBy(message => message.SequenceNumber).ToArray();
            var answer = messages.Last(message => message.MessageType is HelpRequestMessageType.TeacherAnswer or HelpRequestMessageType.FaqAnswer);
            if (request.IsFaq)
                notification.EnqueueIntegrationEvent(new OutgoingMessage(change.Id,
                    new FaqAnsweredMessage(request.Id, request.SessionId, request.Generation,
                        answer.Text.Value, request.AssignedTeacherId!, change.OccurredAt,
                        messages.Single(message => message.MessageType == HelpRequestMessageType.FaqQuestion).Text.Value),
                    MessageRoutes.Requests, MessageRoutes.FaqAnswered));
            else notification.EnqueueIntegrationEvent(new OutgoingMessage(change.Id,
                new HelpRequestAnsweredMessage(request.Id, request.SessionId, request.Generation,
                    messages.Last(message => message.MessageType is HelpRequestMessageType.StudentQuestion or HelpRequestMessageType.FollowUpQuestion).Text.Value,
                    answer.Text.Value),
                MessageRoutes.Requests, MessageRoutes.HelpRequestAnswered));
        }

        // Wait for KnowledgeBase's canonical FAQ answer before notifying any recipient.
        if (change.Change == "FaqSuggested" || request.IsFaq && change.Change == "Answered") return;

        // Event handlers are singletons; keep database dependencies in a local scope.
        await using var scope = scopeFactory.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ICommandHandler<GetHelpRequestSessionQuery, HelpRequestSession?>>();
        var session = await sessions.ExecuteAsync(new GetHelpRequestSessionQuery(request.SessionId), cancellationToken);
        if (session is null || session.Generation != request.Generation) return;

        // Resolved FAQ confirmations leave other students' queue data unchanged.
        // Reservations change the shared unreserved count, so all participants refresh.
        var changesSharedQueue = change.Change switch
        {
            "FaqAccepted" => false,
            "Cancelled" when request.FaqAnswerId is not null => false,
            _ => true
        };
        IEnumerable<string> recipients = session.TeacherIds;
        if (changesSharedQueue)
        {
            var participants = scope.ServiceProvider.GetRequiredService<ICommandHandler<GetSessionParticipantIdsQuery, string[]>>();
            recipients = recipients.Concat(await participants.ExecuteAsync(
                new GetSessionParticipantIdsQuery(request.SessionId), cancellationToken));
        }
        else if (request.StudentId is not null)
            recipients = recipients.Append(request.StudentId);
        foreach (var userId in recipients.Distinct(StringComparer.Ordinal))
            Notify(userId, NotificationEventType.SessionChanged, "Sessionen er opdateret.", NotificationDeliveryType.RealtimeOnly);

        if (change.Change is "Created" or "StudentMessageAdded")
        {
            var repository = scope.ServiceProvider.GetRequiredService<IHelpRequestRepository>();
            var queued = await repository.GetForSessionAsync(request.SessionId, request.Generation, cancellationToken);
            if (!queued.Any(other => other.Id != request.Id && other.Status == HelpRequestStatus.Pending))
                foreach (var teacherId in session.TeacherIds)
                    Notify(teacherId, change.Change == "Created"
                        ? NotificationEventType.HelpRequestCreated : NotificationEventType.StudentMessageAdded,
                        change.Change == "Created"
                        ? "Køen er startet. Der er en ny hjælpeanmodning."
                        : "Køen er opdateret. Der er en opfølgende besked.");
        }
        if (change.Change == "FaqCreated")
            foreach (var teacherId in session.TeacherIds)
                Notify(teacherId, NotificationEventType.FaqCreated, "Der er kommet et nyt generaliseret FAQ-spørgsmål i køen.");
        if (change.Change == "FaqLinked")
            Notify(request.StudentId, NotificationEventType.FaqCreated, "Din hjælpeanmodning indgår nu i et fælles FAQ-spørgsmål.");
        if (change.Change == "Started")
            Notify(request.StudentId, NotificationEventType.HelpRequestStarted, $"Det er din tur til fysisk hjælp. {request.DisplayNumber}.");
        if (change.Change == "Answered")
            Notify(request.StudentId, NotificationEventType.HelpRequestAnswered, "Der er kommet svar på din hjælpeanmodning.");

        void Notify(string? userId, NotificationEventType eventType, string message,
            NotificationDeliveryType deliveryType = NotificationDeliveryType.RealtimeOrPush)
        {
            if (userId is null) return;
            var silent = deliveryType == NotificationDeliveryType.RealtimeOnly;
            var reason = silent && change.Change == "Cancelled" && request.FaqAnswerId is not null
                ? "FaqCancelled" : change.Change;
            // A personal notification replaces the generic update for this recipient in the same commit.
            // Only the owner and session teachers may receive private request details.
            var canReadRequest = userId == request.StudentId || session.TeacherIds.Contains(userId, StringComparer.Ordinal);
            notification.EnqueueIntegrationEvent(new OutgoingMessage(Guid.NewGuid(),
                new NotificationCreatedMessage(userId, message, request.SessionId, request.Generation,
                    canReadRequest ? request.Id : null, reason,
                    canReadRequest ? request.Status.ToString() : null, eventType, deliveryType),
                MessageRoutes.Notifications, MessageRoutes.NotificationCreated,
                CoalescingKey: $"session-notification:{request.SessionId}:{userId}", Priority: silent ? 0 : 1));
        }
    }
}
