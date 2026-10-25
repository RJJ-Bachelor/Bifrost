using System.Text.Json;
using GnaService.Application.Features.Internal.NotificationCreated;
using GnaService.Application.Notifications;
using GnaService.Application.Services.Messaging;
using GnaService.Domain.DomainEvents;
using GnaService.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Application.DomainEvents;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;
using Shared.Domain.Notifications;

namespace GnaService.Test;

public sealed partial class NotificationTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task RealtimeOnlyAttemptsSignalROnceAndNeverUsesPush(bool online, bool connectionFails)
    {
        var realtime = new Sender(online, connectionFails ? new IOException("Disconnected") : null);
        var push = new Sender(true);
        var notification = Message with { DeliveryType = NotificationDeliveryType.RealtimeOnly };

        await new NotificationDispatcher(realtime, push, new Recorder<NotificationDispatcher>())
            .DispatchAsync(notification, default);

        Assert.Equal(1, realtime.Calls);
        Assert.Equal(0, push.Calls);
    }

    [Theory]
    [InlineData(NotificationDeliveryType.RealtimeOrPush)]
    [InlineData(NotificationDeliveryType.RealtimeOnly)]
    public async Task DeliveryTypeSurvivesTheAggregateAndDeliveryOutbox(NotificationDeliveryType deliveryType)
    {
        var incoming = new MessageEnvelope<NotificationCreatedMessage>(Guid.NewGuid(),
            new("student", "Session updated", Guid.NewGuid(), 3,
                EventType: NotificationEventType.SessionChanged, DeliveryType: deliveryType));
        var store = new NotificationStore();
        await new NotificationMessageHandler(NullLogger<NotificationMessageHandler>.Instance, store, store, TimeProvider.System)
            .HandleAsync(incoming, default);
        var notification = Assert.IsType<Notification>(store.Saved);
        Assert.Equal(deliveryType, notification.DeliveryType);
        var domainEvent = Assert.IsType<NotificationCreatedDomainEvent>(Assert.Single(notification.GetDomainEvents()));
        var outgoing = new List<OutgoingMessage>();

        await new NotificationCreatedIntegrationEventHandler().HandleAsync(
            new DomainEventNotification<NotificationCreatedDomainEvent>(domainEvent, outgoing.Add), default);

        var staged = Assert.Single(outgoing);
        var delivery = Assert.IsType<NotificationDeliveryRequestedMessage>(staged.Payload);
        Assert.Equal(incoming.Id, staged.Id);
        Assert.Equal(incoming.Id, delivery.NotificationId);
        Assert.Equal(deliveryType, delivery.DeliveryType);
        Assert.Equal(NotificationEventType.SessionChanged, delivery.EventType);
    }

    [Theory]
    [InlineData(NotificationDeliveryType.RealtimeOrPush, "RealtimeOrPush")]
    [InlineData(NotificationDeliveryType.RealtimeOnly, "RealtimeOnly")]
    public void DeliveryTypesAreStringsOnBothContractsAndRoundtrip(NotificationDeliveryType deliveryType, string name)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var incoming = new NotificationCreatedMessage("student", "Session updated",
            EventType: NotificationEventType.SessionChanged, DeliveryType: deliveryType);
        var delivery = Message with { DeliveryType = deliveryType };
        using var incomingJson = JsonDocument.Parse(JsonSerializer.Serialize(incoming, options));
        using var deliveryJson = JsonDocument.Parse(JsonSerializer.Serialize(delivery, options));

        Assert.Equal(name, incomingJson.RootElement.GetProperty("deliveryType").GetString());
        Assert.Equal(name, deliveryJson.RootElement.GetProperty("deliveryType").GetString());
        Assert.Equal("SessionChanged", incomingJson.RootElement.GetProperty("eventType").GetString());
        Assert.Equal(incoming, incomingJson.RootElement.Deserialize<NotificationCreatedMessage>(options));
        Assert.Equal(delivery, deliveryJson.RootElement.Deserialize<NotificationDeliveryRequestedMessage>(options));
    }

    [Fact]
    public void ExistingPayloadsAndDomainCallersKeepRealtimeThenPushDelivery()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var incoming = JsonSerializer.Deserialize<NotificationCreatedMessage>(
            """{"userId":"student","messages":"Answer"}""", options)!;
        var delivery = JsonSerializer.Deserialize<NotificationDeliveryRequestedMessage>(
            """{"notificationId":"11111111-1111-1111-1111-111111111111","userId":"student","message":"Answer","createdAt":"2026-10-24T12:00:00Z"}""", options)!;
        var notification = Notification.Create(Guid.NewGuid(), "student", "Answer");

        Assert.Equal(NotificationDeliveryType.RealtimeOrPush, incoming.DeliveryType);
        Assert.Equal(NotificationDeliveryType.RealtimeOrPush, delivery.DeliveryType);
        Assert.Equal(NotificationDeliveryType.RealtimeOrPush, notification.DeliveryType);
    }
}
