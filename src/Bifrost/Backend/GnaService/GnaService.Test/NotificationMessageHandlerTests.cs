using GnaService.Application.Notifications;
using GnaService.Application.Repositories;
using GnaService.Application.Services.Messaging;
using GnaService.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Persistence;
using Shared.Domain.Notifications;

namespace GnaService.Test;

public sealed class NotificationMessageHandlerTests
{
    [Theory]
    [InlineData("Created", NotificationEventType.HelpRequestCreated)]
    [InlineData("StudentMessageAdded", NotificationEventType.StudentMessageAdded)]
    [InlineData("Started", NotificationEventType.HelpRequestStarted)]
    [InlineData("Answered", NotificationEventType.HelpRequestAnswered)]
    [InlineData("FaqCreated", NotificationEventType.FaqCreated)]
    [InlineData("FaqSuggested", NotificationEventType.FaqAnswered)]
    [InlineData("Unexpected", NotificationEventType.Unknown)]
    [InlineData(null, NotificationEventType.Unknown)]
    public async Task LegacyMessagesInferAnEventTypeAndUseTheInjectedClock(string? change, NotificationEventType expected)
    {
        var now = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var store = new Store();
        var incoming = new MessageEnvelope<NotificationCreatedMessage>(Guid.NewGuid(), new("student", "Answer", Change: change));
        using var cancellation = new CancellationTokenSource();

        await Handler(store, new FixedTimeProvider(now)).HandleAsync(incoming, cancellation.Token);

        Assert.Equal(expected, store.Saved!.EventType);
        Assert.Equal(incoming.Id, store.Saved.Id);
        Assert.Equal(now, store.Saved.CreatedAt);
        Assert.Equal(1, store.Commits);
        Assert.Equal(cancellation.Token, store.AddToken);
        Assert.Equal(cancellation.Token, store.CommitToken);
    }

    [Fact]
    public async Task DuplicateDeliveryDoesNotCommitAgain()
    {
        var store = new Store { Added = false };

        await Handler(store).HandleAsync(new(Guid.NewGuid(), new("student", "Answer")), default);

        Assert.NotNull(store.Saved);
        Assert.Equal(0, store.Commits);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PersistenceFailureReachesTheCallerSoTheMessageCanBeRetried(bool failDuringAdd)
    {
        var failure = new IOException("Database unavailable");
        var store = new Store { AddFailure = failDuringAdd ? failure : null, CommitFailure = failDuringAdd ? null : failure };

        var thrown = await Assert.ThrowsAsync<IOException>(() =>
            Handler(store).HandleAsync(new(Guid.NewGuid(), new("student", "Answer")), default));

        Assert.Same(failure, thrown);
        Assert.Equal(failDuringAdd ? 0 : 1, store.Commits);
    }

    [Fact]
    public async Task FailedPushDoesNotMakeAnAlreadyStoredNotificationFail()
    {
        var push = new DeliverySender(new IOException("Push failed"));
        var realtime = new DeliverySender();
        var message = new NotificationDeliveryRequestedMessage(Guid.NewGuid(), "student", "Answer", DateTimeOffset.UtcNow);

        await new NotificationDispatcher(realtime, push, NullLogger<NotificationDispatcher>.Instance).DispatchAsync(message, default);

        Assert.Equal(1, realtime.Calls);
        Assert.Equal(1, push.Calls);
    }

    [Fact]
    public async Task PushCancellationIsPropagatedToTheCaller()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var push = new DeliverySender(new OperationCanceledException(cancellation.Token));
        var message = new NotificationDeliveryRequestedMessage(Guid.NewGuid(), "student", "Answer", DateTimeOffset.UtcNow);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new NotificationDispatcher(
            new DeliverySender(), push, NullLogger<NotificationDispatcher>.Instance).DispatchAsync(message, cancellation.Token));

        Assert.Equal(1, push.Calls);
    }

    private static NotificationMessageHandler Handler(Store store, TimeProvider? clock = null) =>
        new(NullLogger<NotificationMessageHandler>.Instance, store, store, clock ?? TimeProvider.System);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Store : INotificationRepository, IUnitOfWork<INotificationRepository>
    {
        public bool Added { get; init; } = true;
        public Exception? AddFailure { get; init; }
        public Exception? CommitFailure { get; init; }
        public Notification? Saved { get; private set; }
        public int Commits { get; private set; }
        public CancellationToken AddToken { get; private set; }
        public CancellationToken CommitToken { get; private set; }
        public Task<bool> TryAddAsync(Notification notification, CancellationToken cancellationToken)
        {
            Saved = notification;
            AddToken = cancellationToken;
            return AddFailure is null ? Task.FromResult(Added) : Task.FromException<bool>(AddFailure);
        }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Commits++;
            CommitToken = cancellationToken;
            return CommitFailure is null ? Task.CompletedTask : Task.FromException(CommitFailure);
        }
    }

    private sealed class DeliverySender(Exception? failure = null) : IRealtimeNotificationSender, IPushNotificationSender
    {
        public int Calls { get; private set; }
        public Task<bool> TrySendAsync(NotificationDeliveryRequestedMessage notification, CancellationToken cancellationToken)
        {
            Calls++;
            return failure is null ? Task.FromResult(false) : Task.FromException<bool>(failure);
        }
    }
}
