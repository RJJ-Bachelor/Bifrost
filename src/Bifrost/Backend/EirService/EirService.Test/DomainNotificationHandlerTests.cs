using EirService.Contracts.HelpRequests.HandleSessionChange;
using EirService.Contracts.Messaging;
using EirService.Contracts.HelpRequests.GetFaqAnswerRecipients;
using EirService.Contracts.Participants.GetSessionParticipantIds;
using EirService.Contracts.Sessions.GetHelpRequestSession;
using EirService.KnowledgeBase.Application.Features.Internal.FaqAnswerRecorded;
using EirService.KnowledgeBase.Domain.DomainEvents;
using EirService.KnowledgeBase.Domain.Entities;
using EirService.Sessions.Application.Features.Internal.SessionArchived;
using EirService.Sessions.Application.Features.Internal.SessionCleared;
using EirService.Sessions.Application.Features.Internal.SessionDeleted;
using EirService.Sessions.Application.Features.Internal.SessionRenamed;
using EirService.Sessions.Application.Repositories;
using EirService.Sessions.Domain.DomainEvents;
using EirService.Sessions.Domain.Entities;
using EirService.Sessions.Domain.ValueObjects;
using FastEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.DomainEvents;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;
using Shared.Domain.Notifications;

namespace EirService.Test;

public sealed class DomainNotificationHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Cleared", true)]
    [InlineData("Archived", true)]
    [InlineData("Deleted", true)]
    [InlineData("Renamed", true)]
    [InlineData("Deleted", false)]
    public async Task LifecycleEventsNotifyEachParticipantAndTheOwnerOnce(string change, bool sessionExists)
    {
        var session = Session.Create("Course", new SessionCode("ABCD2345"), new SessionCode("EFGH6789"), "owner", null, Now);
        var repository = new MemorySessionRepository();
        if (sessionExists) await repository.TryAddAsync(session, default);
        var participantQuery = new StubCommandHandler<GetSessionParticipantIdsQuery, string[]>(["student", "student", "colleague"]);
        var services = new ServiceCollection();
        services.AddSingleton<ISessionRepository>(repository);
        services.AddSingleton<ICommandHandler<GetSessionParticipantIdsQuery, string[]>>(participantQuery);
        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var outgoing = new List<OutgoingMessage>();
        Guid? eventId = null;

        switch (change)
        {
            case "Cleared":
                var cleared = new SessionClearedDomainEvent(session.Id, 1, 2, Now);
                eventId = cleared.Id;
                await new SessionClearedIntegrationEventUpgradeHandler(scopeFactory).HandleAsync(new(cleared, outgoing.Add), default);
                break;
            case "Archived":
                var archived = new SessionArchivedDomainEvent(session.Id, 1, 2, Now);
                eventId = archived.Id;
                await new SessionArchivedIntegrationEventUpgradeHandler(scopeFactory).HandleAsync(new(archived, outgoing.Add), default);
                break;
            case "Deleted":
                var deleted = new SessionDeletedDomainEvent(session.Id, 2, Now);
                eventId = deleted.Id;
                await new SessionDeletedIntegrationEventUpgradeHandler(scopeFactory).HandleAsync(new(deleted, outgoing.Add), default);
                break;
            case "Renamed":
                await new SessionRenamedNotificationHandler(scopeFactory).HandleAsync(new(new(session.Id, 2), outgoing.Add), default);
                break;
        }

        Assert.Equal(session.Id, Assert.Single(participantQuery.Commands).SessionId);
        var broadcast = Assert.Single(outgoing, message => message.Payload is SessionChangedMessage);
        var audience = Assert.IsType<SessionChangedMessage>(broadcast.Payload);
        var expectedRecipients = sessionExists ? new[] { "colleague", "owner", "student" } : new[] { "colleague", "student" };
        Assert.Equal(expectedRecipients, audience.UserIds.Order());
        Assert.Equal(outgoing.Count, outgoing.Select(message => (message.ExchangeName, message.RoutingKey, message.Id)).Distinct().Count());
        Assert.Equal((session.Id, 2, change), (audience.SessionId, audience.Generation, audience.Change));
        Assert.Equal(MessageRoutes.Notifications, broadcast.ExchangeName);
        Assert.Equal(MessageRoutes.SessionChanged, broadcast.RoutingKey);
        Assert.DoesNotContain(outgoing, message => message.Payload is NotificationCreatedMessage);
        if (change == "Renamed") Assert.Single(outgoing);
        else
        {
            var integration = Assert.Single(outgoing, message => message.Payload is SessionUpdatedIntegrationEvent);
            var payload = Assert.IsType<SessionUpdatedIntegrationEvent>(integration.Payload);
            Assert.Equal(eventId, integration.Id);
            Assert.Equal(session.Id, payload.SessionId);
            Assert.Equal(2, payload.Generation);
            Assert.Equal(Enum.Parse<SessionUpdateType>(change), payload.EventType);
            Assert.Equal(change == "Deleted" ? null : (int?)1, payload.PreviousGeneration);
            Assert.Equal(MessageRoutes.SessionUpdated, integration.RoutingKey);
            var internalMessage = Assert.Single(outgoing, message => message.Payload is HandleSessionChangeCommand);
            Assert.Equal(integration.Id, internalMessage.Id);
            Assert.Equal(InternalMessageRoutes.Exchange, internalMessage.ExchangeName);
            Assert.Equal(EirModuleRoutes.SessionChanged, internalMessage.RoutingKey);
            Assert.Equal(new HandleSessionChangeCommand(payload.EventId, payload.SessionId, payload.Generation,
                Enum.Parse<SessionChangeType>(change), payload.UpdatedAtUtc, payload.PreviousGeneration), internalMessage.Payload);
            Assert.Equal(outgoing.Count, OutgoingMessage.Coalesce(outgoing).Count());
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FaqAnswerRefreshesTheCurrentGenerationAndOnlyPromptsCurrentStudents(int sessionGeneration)
    {
        var faq = FaqAnswer.CreateAnswered(Guid.NewGuid(), 1, "Question", "Answer", "owner", Now);
        var sessionQuery = new StubCommandHandler<GetHelpRequestSessionQuery, HelpRequestSession?>(new(faq.SessionId, sessionGeneration, ["owner", "colleague"]));
        var participants = new StubCommandHandler<GetSessionParticipantIdsQuery, string[]>(["owner", "student", "student"]);
        var requestId = Guid.NewGuid();
        var recipients = new StubCommandHandler<GetFaqAnswerRecipientsQuery, IReadOnlyList<FaqAnswerRecipient>>([new("student", requestId, "AwaitingFaq")]);
        var services = new ServiceCollection();
        services.AddSingleton<ICommandHandler<GetHelpRequestSessionQuery, HelpRequestSession?>>(sessionQuery);
        services.AddSingleton<ICommandHandler<GetSessionParticipantIdsQuery, string[]>>(participants);
        services.AddSingleton<ICommandHandler<GetFaqAnswerRecipientsQuery, IReadOnlyList<FaqAnswerRecipient>>>(recipients);
        await using var provider = services.BuildServiceProvider();
        var outgoing = new List<OutgoingMessage>();

        await new FaqAnswerRecordedIntegrationEventHandler(provider.GetRequiredService<IServiceScopeFactory>())
            .HandleAsync(new(new FaqAnswerRecordedDomainEvent(faq), outgoing.Add), default);

        var generic = outgoing.Where(message => message.Priority == 0).ToArray();
        Assert.Equal(new[] { "colleague", "owner", "student" }, generic.Select(message => ((NotificationCreatedMessage)message.Payload).UserId).Order());
        Assert.All(generic, message =>
        {
            var payload = Assert.IsType<NotificationCreatedMessage>(message.Payload);
            Assert.Equal(sessionGeneration, payload.Generation);
            Assert.Equal(sessionGeneration == 1 ? "FaqAnswered" : "ArchivedFaqAnswered", payload.Change);
            Assert.Equal(NotificationDeliveryType.RealtimeOnly, payload.DeliveryType);
        });
        if (sessionGeneration == 1)
        {
            var personal = Assert.Single(outgoing, message => message.Priority == 1);
            var payload = Assert.IsType<NotificationCreatedMessage>(personal.Payload);
            Assert.Equal(("student", requestId, "AwaitingFaq", NotificationEventType.FaqAnswered, NotificationDeliveryType.RealtimeOrPush),
                (payload.UserId, payload.HelpRequestId!.Value, payload.Status, payload.EventType, payload.DeliveryType));
            Assert.Equal(new GetFaqAnswerRecipientsQuery(faq.Id, faq.SessionId, faq.Generation), Assert.Single(recipients.Commands));
            var coalesced = OutgoingMessage.Coalesce(outgoing).ToArray();
            Assert.Equal(3, coalesced.Length);
            Assert.Contains(personal, coalesced);
        }
        else
        {
            Assert.Empty(recipients.Commands);
            Assert.Equal(3, outgoing.Count);
        }
    }

    [Fact]
    public async Task AnswerForADeletedSessionStagesNoNotifications()
    {
        var faq = FaqAnswer.CreateAnswered(Guid.NewGuid(), 1, "Question", "Answer", "owner", Now);
        var services = new ServiceCollection();
        services.AddSingleton<ICommandHandler<GetHelpRequestSessionQuery, HelpRequestSession?>>(
            new StubCommandHandler<GetHelpRequestSessionQuery, HelpRequestSession?>(null));
        await using var provider = services.BuildServiceProvider();
        var outgoing = new List<OutgoingMessage>();

        await new FaqAnswerRecordedIntegrationEventHandler(provider.GetRequiredService<IServiceScopeFactory>())
            .HandleAsync(new(new FaqAnswerRecordedDomainEvent(faq), outgoing.Add), default);

        Assert.Empty(outgoing);
    }

    private sealed class StubCommandHandler<TCommand, TResult>(TResult result) : ICommandHandler<TCommand, TResult>
        where TCommand : ICommand<TResult>
    {
        public List<TCommand> Commands { get; } = [];
        public Task<TResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            return Task.FromResult(result);
        }
    }
}
