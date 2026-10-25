using EirService.Contracts.HelpRequests.GetFaqAnswerRecipients;
using EirService.Contracts.Participants.GetSessionParticipantIds;
using EirService.Contracts.Sessions.GetHelpRequestSession;
using EirService.KnowledgeBase.Domain.DomainEvents;
using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.DomainEvents;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;
using Shared.Domain.Notifications;

namespace EirService.KnowledgeBase.Application.Features.Internal.FaqAnswerRecorded;

public sealed class FaqAnswerRecordedIntegrationEventHandler(IServiceScopeFactory scopeFactory)
    : IEventHandler<DomainEventNotification<FaqAnswerRecordedDomainEvent>>
{
    public async Task HandleAsync(DomainEventNotification<FaqAnswerRecordedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var faq = notification.DomainEvent.Faq;
        // Stage notifications with the canonical answer; the outbox publishes only after both commit.
        await using var scope = scopeFactory.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ICommandHandler<GetHelpRequestSessionQuery, HelpRequestSession?>>();
        var session = await sessions.ExecuteAsync(new GetHelpRequestSessionQuery(faq.SessionId), cancellationToken);
        if (session is null) return;
        var participants = scope.ServiceProvider.GetRequiredService<ICommandHandler<GetSessionParticipantIdsQuery, string[]>>();
        var participantIds = await participants.ExecuteAsync(new GetSessionParticipantIdsQuery(faq.SessionId), cancellationToken);
        foreach (var userId in participantIds.Concat(session.TeacherIds).Distinct(StringComparer.Ordinal))
            Notify(new NotificationCreatedMessage(userId, "Sessionen er opdateret.", faq.SessionId, session.Generation,
                Change: session.Generation == faq.Generation ? "FaqAnswered" : "ArchivedFaqAnswered",
                EventType: NotificationEventType.SessionChanged,
                DeliveryType: NotificationDeliveryType.RealtimeOnly), priority: 0);

        // An answer from an archived generation refreshes the archive without prompting an old confirmation.
        if (session.Generation != faq.Generation) return;
        var recipients = scope.ServiceProvider.GetRequiredService<ICommandHandler<GetFaqAnswerRecipientsQuery, IReadOnlyList<FaqAnswerRecipient>>>();
        var students = await recipients.ExecuteAsync(new GetFaqAnswerRecipientsQuery(faq.Id, faq.SessionId, faq.Generation), cancellationToken);
        foreach (var recipient in students)
            Notify(new NotificationCreatedMessage(recipient.StudentId,
                "Der er kommet et fælles FAQ-svar. Bekræft om det hjælper dig.", faq.SessionId, faq.Generation,
                recipient.HelpRequestId, "FaqSuggested", recipient.Status, NotificationEventType.FaqAnswered,
                NotificationDeliveryType.RealtimeOrPush), priority: 1);

        void Notify(NotificationCreatedMessage message, int priority) =>
            notification.EnqueueIntegrationEvent(new OutgoingMessage(Guid.NewGuid(), message,
                MessageRoutes.Notifications, MessageRoutes.NotificationCreated,
                CoalescingKey: $"session-notification:{faq.SessionId}:{message.UserId}", Priority: priority));
    }
}