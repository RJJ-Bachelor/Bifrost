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
            var text = request.Messages.OrderBy(message => message.SequenceNumber).First().Text.Value;
            notification.EnqueueIntegrationEvent(new OutgoingMessage(request.Id,
                new HelpRequestCreatedMessage(request.Id, request.SessionId, request.Generation, text),
                MessageRoutes.Requests, MessageRoutes.HelpRequestCreated));
        }
        if (change.Change is "Created" or "StudentMessageAdded")
        {
            // Event handlers are singletons; keep database dependencies in a local scope.
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IHelpRequestRepository>();
            var queued = await repository.GetForSessionAsync(request.SessionId, request.Generation, cancellationToken);
            if (!queued.Any(other => other.Id != request.Id && other.Status == HelpRequestStatus.Pending))
            {
                var sessions = scope.ServiceProvider.GetRequiredService<ICommandHandler<GetHelpRequestSessionQuery, HelpRequestSession?>>();
                var session = await sessions.ExecuteAsync(new GetHelpRequestSessionQuery(request.SessionId), cancellationToken);
                if (session is not null && session.Generation == request.Generation)
                    foreach (var teacherId in session.TeacherIds)
                        Notify(teacherId, change.Change == "Created"
                            ? "Køen er startet. Der er en ny hjælpeanmodning."
                            : "Køen er opdateret. Der er en opfølgende besked.");
            }
        }
        if (change.Change == "Started")
            Notify(request.StudentId, $"Det er din tur til fysisk hjælp. {request.DisplayNumber}.");
        if (change.Change == "Answered")
        {
            var messages = request.Messages.OrderBy(message => message.SequenceNumber).ToArray();
            notification.EnqueueIntegrationEvent(new OutgoingMessage(change.Id,
                new HelpRequestAnsweredMessage(request.Id, request.SessionId, request.Generation,
                    messages[^2].Text.Value, messages[^1].Text.Value),
                MessageRoutes.Requests, MessageRoutes.HelpRequestAnswered));
            Notify(request.StudentId, "Der er kommet svar på din hjælpeanmodning.");
        }

        // Each recipient needs a separate message id for downstream inbox deduplication.
        void Notify(string userId, string message) => notification.EnqueueIntegrationEvent(new OutgoingMessage(Guid.NewGuid(),
            new NotificationCreatedMessage(userId, message, request.SessionId, request.Generation,
                request.Id, change.Change, request.Status.ToString()),
            MessageRoutes.Notifications, MessageRoutes.NotificationCreated));
    }
}
