using EirService.KnowledgeBase.Domain.DomainEvents;
using FastEndpoints;
using Shared.Application.DomainEvents;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;

namespace EirService.KnowledgeBase.Application.Features.Internal.FaqQuestionChanged;

public sealed class FaqQuestionChangedIntegrationEventHandler
    : IEventHandler<DomainEventNotification<FaqQuestionChangedDomainEvent>>
{
    public Task HandleAsync(DomainEventNotification<FaqQuestionChangedDomainEvent> notification, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var change = notification.DomainEvent;
        var faq = change.Faq;
        notification.EnqueueIntegrationEvent(OutgoingMessage.Event(change.Id,
            new FaqQuestionChangedIntegrationEvent(faq.Id, faq.SessionId, faq.Generation,
                change.PreviousQuestion, faq.Question, faq.Answer!, faq.AnsweredByTeacherId!, faq.AnsweredAt!.Value),
            MessageRoutes.Requests, MessageRoutes.FaqQuestionChanged, faq.AnsweredAt.Value));
        return Task.CompletedTask;
    }
}
