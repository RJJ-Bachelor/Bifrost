using GnaService.Application.Features.Internal.NotificationCreated;
using GnaService.Application.Notifications;
using GnaService.Application.Repositories;
using GnaService.Application.Services.Messaging;
using GnaService.Domain.DomainEvents;
using GnaService.Domain.Entities;
using GnaService.Infrastructure.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Application.DomainEvents;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;
using Shared.Application.Persistence;

namespace GnaService.Test;

public sealed class NotificationTests
{
    private static readonly NotificationDeliveryRequestedMessage Message = new(Guid.NewGuid(), "student", "Help", DateTimeOffset.UtcNow);

    [Fact]
    public async Task OnlineUserUsesSignalRWithoutPush()
    {
        var realtime = new Sender(true);
        var push = new Sender(true);
        var logger = new Recorder<NotificationDispatcher>();
        await new NotificationDispatcher(realtime, push, logger).DispatchAsync(Message, default);
        Assert.Equal(1, realtime.Calls);
        Assert.Equal(0, push.Calls);
        Assert.Contains(logger.Messages, message => message.Contains("SignalR") && message.Contains(Message.UserId));
    }

    [Fact]
    public async Task OfflineUserFallsBackToPush()
    {
        var push = new Sender(true);
        await new NotificationDispatcher(new Sender(false), push, new Recorder<NotificationDispatcher>()).DispatchAsync(Message, default);
        Assert.Equal(1, push.Calls);
    }

    [Fact]
    public async Task FailedSignalRFallsBackAndLogsBothChannels()
    {
        var logger = new Recorder<NotificationDispatcher>();
        var push = new Sender(true);
        await new NotificationDispatcher(new Sender(false, new IOException("Disconnected")), push, logger).DispatchAsync(Message, default);
        Assert.Equal(1, push.Calls);
        Assert.Contains(logger.Messages, message => message.Contains("SignalR failed"));
        Assert.Contains(logger.Messages, message => message.Contains("sent through Push"));
    }

    [Fact]
    public async Task NoChannelIsLoggedWithNotificationAndUser()
    {
        var logger = new Recorder<NotificationDispatcher>();
        await new NotificationDispatcher(new Sender(false), new Sender(false), logger).DispatchAsync(Message, default);
        Assert.Contains(logger.Messages, message => message.Contains("no delivery channel") && message.Contains(Message.NotificationId.ToString())
            && message.Contains(Message.UserId) && message.Contains(Message.Message));
    }

    [Fact]
    public async Task CancellationDoesNotStartPush()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var push = new Sender(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => new NotificationDispatcher(
            new Sender(false, new OperationCanceledException(cancellation.Token)), push, new Recorder<NotificationDispatcher>())
            .DispatchAsync(Message, cancellation.Token));
        Assert.Equal(0, push.Calls);
    }

    [Fact]
    public void DisconnectingOneTabKeepsTheOtherRegistered()
    {
        var connections = new ConnectionRegistry();
        connections.Add("student", "tab-1");
        connections.Add("student", "tab-2");
        connections.Add("other", "tab-3");
        connections.Remove("tab-1");
        Assert.True(connections.IsConnected("student"));
        connections.Remove("tab-2");
        Assert.False(connections.IsConnected("student"));
        Assert.True(connections.IsConnected("other"));
    }

    [Fact]
    public async Task IncomingNotificationStagesDeliveryWithTheOriginalRecipientAndContext()
    {
        var incoming = new MessageEnvelope<NotificationCreatedMessage>(Guid.NewGuid(),
            new("student", "Hej fra backenden", Guid.NewGuid(), 2, Guid.NewGuid(), "Answered", "Completed"));
        var store = new NotificationStore();
        await new NotificationMessageHandler(NullLogger<NotificationMessageHandler>.Instance, store, store, TimeProvider.System)
            .HandleAsync(incoming, default);
        var created = Assert.IsType<Notification>(store.Saved);
        Assert.Equal(1, store.Commits);
        var domainEvent = Assert.IsType<NotificationCreatedDomainEvent>(Assert.Single(created.GetDomainEvents()));
        var outgoing = new List<OutgoingMessage>();
        await new NotificationCreatedIntegrationEventHandler().HandleAsync(
            new DomainEventNotification<NotificationCreatedDomainEvent>(domainEvent, outgoing.Add), default);
        var staged = Assert.Single(outgoing);
        var payload = Assert.IsType<NotificationDeliveryRequestedMessage>(staged.Payload);
        Assert.Equal(new NotificationDeliveryRequestedMessage(incoming.Id, incoming.Payload.UserId, incoming.Payload.Messages,
            created.CreatedAt, incoming.Payload.SessionId, incoming.Payload.HelpRequestId), payload);
        Assert.Equal(MessageRoutes.NotificationDeliveryRequested, staged.RoutingKey);
    }

    [Fact]
    public void PushSubscriptionsRemainBoundToTheUserWhenKeysChange()
    {
        var subscription = PushSubscription.Create("student", "https://push.example/subscription", "public-key", "auth");
        subscription.UpdateKeys("replacement-key", "replacement-auth");
        Assert.Equal("student", subscription.UserId.Value);
        Assert.Equal("replacement-key", subscription.P256dh);
        Assert.Throws<ArgumentException>(() => PushSubscription.Create("student", "http://push.example", "key", "auth"));
    }

    private sealed class Sender(bool result, Exception? failure = null) : IRealtimeNotificationSender, IPushNotificationSender
    {
        public int Calls { get; private set; }
        public Task<bool> TrySendAsync(NotificationDeliveryRequestedMessage notification, CancellationToken cancellationToken)
        {
            Calls++;
            return failure is null ? Task.FromResult(result) : Task.FromException<bool>(failure);
        }
    }

    private sealed class NotificationStore : INotificationRepository, IUnitOfWork<INotificationRepository>
    {
        public Notification? Saved { get; private set; }
        public int Commits { get; private set; }

        public Task<bool> TryAddAsync(Notification notification, CancellationToken cancellationToken)
        {
            Saved = notification;
            return Task.FromResult(true);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Commits++;
            return Task.CompletedTask;
        }
    }

    private sealed class Recorder<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
