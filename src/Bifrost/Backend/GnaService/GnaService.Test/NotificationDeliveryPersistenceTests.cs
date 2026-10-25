using System.Text.Json;
using GnaService.Application.Services.Messaging;
using GnaService.Domain.Entities;
using GnaService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;
using Shared.Domain.Notifications;
using Shared.Infrastructure.Persistence.Outbox;

namespace GnaService.Test;

public sealed partial class NotificationPersistenceTests
{
    [GnaPostgresFact]
    public async Task RealtimeOnlyPersistsAndStagesOneDeliveryEvenWhenRedelivered()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<INotificationMessageHandler>();
        var incoming = new MessageEnvelope<NotificationCreatedMessage>(Guid.NewGuid(),
            new("student", "Session updated", Guid.NewGuid(), 2,
                EventType: NotificationEventType.SessionChanged, DeliveryType: NotificationDeliveryType.RealtimeOnly));

        await handler.HandleAsync(incoming, default);
        await handler.HandleAsync(incoming, default);

        var db = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
        db.ChangeTracker.Clear();
        var saved = Assert.Single(await db.Notifications.ToArrayAsync());
        var staged = Assert.Single(await db.Outbox.ToArrayAsync());
        var delivery = JsonSerializer.Deserialize<NotificationDeliveryRequestedMessage>(staged.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(incoming.Id, saved.Id);
        Assert.Equal(NotificationDeliveryType.RealtimeOnly, saved.DeliveryType);
        Assert.Equal(NotificationEventType.SessionChanged, saved.EventType);
        Assert.Equal(incoming.Id, delivery.NotificationId);
        Assert.Equal(NotificationDeliveryType.RealtimeOnly, delivery.DeliveryType);
        Assert.Equal(NotificationEventType.SessionChanged, delivery.EventType);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [GnaPostgresFact]
    public async Task DeliveryTypeMigrationDefaultsHistoryAndLeavesLegacyOutboxDeliveriesCompatible()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
        var notification = Notification.Create(Guid.NewGuid(), "student", "Answer",
            eventType: NotificationEventType.HelpRequestAnswered);
        db.Notifications.Add(notification);
        var payload = new NotificationDeliveryRequestedMessage(notification.Id, notification.UserId.Value,
            notification.Messages.Value, notification.CreatedAt);
        var pending = OutboxMessage.Create(new OutgoingMessage(notification.Id, payload,
            MessageRoutes.Notifications, MessageRoutes.NotificationDeliveryRequested), notification.CreatedAt);
        var published = OutboxMessage.Create(new OutgoingMessage(notification.Id, payload,
            MessageRoutes.Notifications, MessageRoutes.NotificationDeliveryRequested), notification.CreatedAt);
        published.MarkPublished(notification.CreatedAt);
        db.Outbox.AddRange(pending, published);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE outbox SET "Payload" = ("Payload"::jsonb - 'deliveryType')::text;
            """);
        await db.GetService<IMigrator>().MigrateAsync("20261022173023_AddNotificationEventType");

        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(NotificationDeliveryType.RealtimeOrPush, (await db.Notifications.SingleAsync()).DeliveryType);
        foreach (var entry in await db.Outbox.ToArrayAsync())
        {
            using var json = JsonDocument.Parse(entry.Payload);
            Assert.False(json.RootElement.TryGetProperty("deliveryType", out _));
            var delivery = json.RootElement.Deserialize<NotificationDeliveryRequestedMessage>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(NotificationDeliveryType.RealtimeOrPush, delivery.DeliveryType);
        }
        Assert.Null((await db.Outbox.SingleAsync(entry => entry.Id == pending.Id)).PublishedAtUtc);
        Assert.NotNull((await db.Outbox.SingleAsync(entry => entry.Id == published.Id)).PublishedAtUtc);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
