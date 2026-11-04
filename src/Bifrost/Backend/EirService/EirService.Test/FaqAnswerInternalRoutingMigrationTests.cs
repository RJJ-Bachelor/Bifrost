using EirService.Contracts.KnowledgeBase.RecordFaqAnswer;
using EirService.Contracts.Messaging;
using EirService.HelpRequests.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Outbox;
using Shared.Infrastructure.Persistence.Outbox;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [PostgresFact]
    public async Task FaqAnswerMigrationReplaysPendingAndBrokerPublishedAnswersLocallyWithoutChangingTheirPayload()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        var migrator = db.GetService<IMigrator>();
        const string previousMigration = "20261027090000_DistinguishHelpRequestCompletion";
        await migrator.MigrateAsync(previousMigration);
        var now = clock.Now;
        OutboxMessage LegacyAnswer(string route = "faq.answered") => OutboxMessage.Create(
            new OutgoingMessage(Guid.NewGuid(), new RecordFaqAnswerCommand(Guid.NewGuid(), Guid.NewGuid(),
                1, "Preserve the answer", "teacher", now, "Preserve the question"), MessageRoutes.Requests, route), now);
        var pending = LegacyAnswer();
        pending.MarkFailed(now.AddMinutes(1));
        var brokerPublished = LegacyAnswer();
        brokerPublished.MarkPublished(now.AddMinutes(2));
        var unrelated = LegacyAnswer(MessageRoutes.HelpRequestAnswered);
        unrelated.MarkPublished(now.AddMinutes(3));
        db.Outbox.AddRange(pending, brokerPublished, unrelated);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE helprequests.outbox
            SET "MessageType" = 'Shared.Application.Messaging.Contracts.FaqAnsweredMessage'
            WHERE "ExchangeName" = 'requests' AND "RoutingKey" = 'faq.answered';
            """);
        db.ChangeTracker.Clear();
        var before = await db.Outbox.AsNoTracking().ToDictionaryAsync(message => message.Id);

        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();

        var after = await db.Outbox.AsNoTracking().ToDictionaryAsync(message => message.Id);
        foreach (var id in new[] { pending.Id, brokerPublished.Id })
        {
            var original = before[id];
            var migrated = after[id];
            Assert.Equal(InternalMessageRoutes.Exchange, migrated.ExchangeName);
            Assert.Equal(EirModuleRoutes.FaqAnswered, migrated.RoutingKey);
            Assert.Equal(typeof(RecordFaqAnswerCommand).FullName, migrated.MessageType);
            Assert.Null(migrated.PublishedAtUtc);
            Assert.Null(migrated.LastAttemptAtUtc);
            Assert.Equal(original.MessageId, migrated.MessageId);
            Assert.Equal(original.Payload, migrated.Payload);
            Assert.Equal(original.CreatedAtUtc, migrated.CreatedAtUtc);
            Assert.Equal(original.TraceParent, migrated.TraceParent);
            Assert.Equal(original.TraceState, migrated.TraceState);
        }
        var unchanged = after[unrelated.Id];
        Assert.Equal(unrelated.ExchangeName, unchanged.ExchangeName);
        Assert.Equal(unrelated.RoutingKey, unchanged.RoutingKey);
        Assert.Equal(unrelated.MessageType, unchanged.MessageType);
        Assert.Equal(unrelated.Payload, unchanged.Payload);
        Assert.Equal(unrelated.PublishedAtUtc, unchanged.PublishedAtUtc);
        Assert.Equal(unrelated.LastAttemptAtUtc, unchanged.LastAttemptAtUtc);
        Assert.False(db.Database.HasPendingModelChanges());

        await migrator.MigrateAsync(previousMigration);
        db.ChangeTracker.Clear();
        var reverted = await db.Outbox.AsNoTracking().ToDictionaryAsync(message => message.Id);
        foreach (var id in new[] { pending.Id, brokerPublished.Id })
        {
            Assert.Equal(MessageRoutes.Requests, reverted[id].ExchangeName);
            Assert.Equal("faq.answered", reverted[id].RoutingKey);
            Assert.Equal("Shared.Application.Messaging.Contracts.FaqAnsweredMessage", reverted[id].MessageType);
            Assert.Equal(before[id].Payload, reverted[id].Payload);
            Assert.Null(reverted[id].PublishedAtUtc);
        }
        Assert.Equal(unrelated.PublishedAtUtc, reverted[unrelated.Id].PublishedAtUtc);
        await db.Database.MigrateAsync();
    }
}
