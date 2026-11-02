using System.Diagnostics;
using System.Text.Json;
using Shared.Application.DomainEvents;
using Shared.Application.Messaging.Outbox;
using Shared.Domain.DomainEvents;
using Shared.Domain.Primitives;
using Shared.Infrastructure.Persistence.Outbox;

namespace Shared.Test;

public sealed class DomainAndOutboxContractTests
{
    [Fact]
    public void AggregateEventsAreSnapshotsAndAcknowledgingOneEventPreservesNewEvents()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());
        var first = new TestEvent(Guid.NewGuid());
        var second = new TestEvent(Guid.NewGuid());
        aggregate.Raise(first);
        var snapshot = aggregate.GetDomainEvents();
        aggregate.Raise(second);

        Assert.Equal(first, Assert.Single(snapshot));
        aggregate.RemoveDomainEvents(new HashSet<Guid> { first.Id, Guid.NewGuid() });
        Assert.Equal(second, Assert.Single(aggregate.GetDomainEvents()));
        aggregate.ClearDomainEvents();
        Assert.Empty(aggregate.GetDomainEvents());
        Assert.NotEqual(Guid.Empty, aggregate.Id);
        Assert.Throws<ArgumentNullException>(() => aggregate.Raise(null!));
    }

    [Fact]
    public void CoalescingKeepsHighestPriorityPerKeyAndEveryUnkeyedMessage()
    {
        var first = Message("first", "session:1", 1);
        var winner = Message("answer", "session:1", 10);
        var tiedLater = Message("later", "session:1", 10);
        var other = Message("other", "session:2", 0);
        var unkeyed = Message("unkeyed");
        var duplicateUnkeyed = unkeyed with { Id = Guid.NewGuid() };

        var result = OutgoingMessage.Coalesce([first, winner, tiedLater, other, unkeyed, duplicateUnkeyed]).ToArray();

        Assert.Equal(new[] { winner, other, unkeyed, duplicateUnkeyed }, result);
        Assert.Empty(OutgoingMessage.Coalesce([]));
    }

    [Fact]
    public void DomainNotificationStagesMessagesWithoutPublishingOrChangingTheDomainEvent()
    {
        var domainEvent = new TestEvent(Guid.NewGuid());
        var outgoing = new List<OutgoingMessage>();
        var notification = new DomainEventNotification<TestEvent>(domainEvent, outgoing.Add);
        var message = Message("Answer");

        notification.EnqueueIntegrationEvent(message);

        Assert.Same(domainEvent, notification.DomainEvent);
        Assert.Same(message, Assert.Single(outgoing));
    }

    [Fact]
    public void OutboxCapturesWirePayloadIdentityUtcTimeAndTheOriginatingTrace()
    {
        using var activity = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.TraceStateString = "vendor=state";
        var now = new DateTimeOffset(2026, 10, 24, 14, 0, 0, TimeSpan.FromHours(2));
        var payload = new TestPayload("Answer", 3);
        var message = new OutgoingMessage(Guid.NewGuid(), payload, "notifications", "created");

        var outbox = OutboxMessage.Create(message, now);

        Assert.NotEqual(Guid.Empty, outbox.Id);
        Assert.Equal(message.Id, outbox.MessageId);
        Assert.Equal(typeof(TestPayload).FullName, outbox.MessageType);
        Assert.Equal("notifications", outbox.ExchangeName);
        Assert.Equal("created", outbox.RoutingKey);
        Assert.Equal(now.ToUniversalTime(), outbox.CreatedAtUtc);
        Assert.Equal(TimeSpan.Zero, outbox.CreatedAtUtc.Offset);
        Assert.Equal(activity.Id, outbox.TraceParent);
        Assert.Equal(activity.TraceStateString, outbox.TraceState);
        using var json = JsonDocument.Parse(outbox.Payload);
        Assert.Equal("Answer", json.RootElement.GetProperty("message").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("generation").GetInt32());
        Assert.Null(outbox.PublishedAtUtc);
        Assert.Null(outbox.LastAttemptAtUtc);
    }

    [Fact]
    public void FailedOutboxAttemptRemainsPendingUntilASuccessfulRetry()
    {
        var now = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var outbox = OutboxMessage.Create(Message("Answer"), now);

        outbox.MarkFailed(now.AddMinutes(1));
        Assert.Null(outbox.PublishedAtUtc);
        Assert.Equal(now.AddMinutes(1), outbox.LastAttemptAtUtc);

        var completed = now.AddMinutes(2).ToOffset(TimeSpan.FromHours(2));
        outbox.MarkPublished(completed);
        Assert.Equal(completed.ToUniversalTime(), outbox.PublishedAtUtc);
        Assert.Equal(outbox.PublishedAtUtc, outbox.LastAttemptAtUtc);
        Assert.Equal(TimeSpan.Zero, outbox.PublishedAtUtc!.Value.Offset);
    }

    [Fact]
    public void InvalidOutboxMessagesAreRejectedBeforePersistence()
    {
        var message = Message("Answer");
        Assert.Throws<ArgumentException>(() => OutboxMessage.Create(message with { Id = Guid.Empty }, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentNullException>(() => OutboxMessage.Create(message with { Payload = null! }, DateTimeOffset.UtcNow));
        Assert.ThrowsAny<ArgumentException>(() => OutboxMessage.Create(message with { ExchangeName = " " }, DateTimeOffset.UtcNow));
        Assert.ThrowsAny<ArgumentException>(() => OutboxMessage.Create(message with { RoutingKey = "" }, DateTimeOffset.UtcNow));
    }

    private static OutgoingMessage Message(string payload, string? key = null, int priority = 0) =>
        new(Guid.NewGuid(), payload, "exchange", "route", key, priority);

    public sealed record TestPayload(string Message, int Generation);
    private sealed record TestEvent(Guid Id) : DomainEvent(Id);
    private sealed class TestAggregate(Guid id) : AggregateRoot<Guid>(id)
    {
        public void Raise(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
    }
}
