using System.Text.Json;
using EirService.Sessions.Application.Repositories;
using EirService.Sessions.Infrastructure;
using EirService.Sessions.Infrastructure.Persistence;
using FastEndpoints;
using GnaService.Application.Services.Messaging;
using GnaService.Infrastructure;
using GnaService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Persistence;
using Shared.Domain.Notifications;
using Shared.Infrastructure.Persistence.Migrations;
using Shared.Infrastructure.Persistence.Outbox;
using Shared.Application.Messaging.Outbox;
using Shared.Testing;

namespace Backend.IntegrationTests.Messaging;

/// <summary>Actual PostgreSQL migrations and AMQP transport; requires isolated external test servers.</summary>
[Collection("Backend hosts")]
public sealed class PostgresRabbitMqPipelineTests
{
    [PostgresRabbitMqFact]
    [Trait("Category", "ExternalIntegration")]
    public async Task CommittedOutboxNotificationTravelsThroughRabbitMqAndIsStoredOnceByGna()
    {
        var postgres = Environment.GetEnvironmentVariable("BIFROST_TEST_POSTGRES")!;
        var rabbit = Environment.GetEnvironmentVariable("BIFROST_TEST_RABBITMQ")!;
        var suffix = Guid.NewGuid().ToString("N");
        await using var eirDatabase = new PostgresTestDatabase("bifrost_integration_eir");
        await using var gnaDatabase = new PostgresTestDatabase("bifrost_integration_gna");
        var queue = "bifrost-integration-" + suffix;
        var queueAttempted = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;

        try
        {
            await eirDatabase.InitializeAsync(postgres, token);
            await gnaDatabase.InitializeAsync(postgres, token);

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:eir"] = eirDatabase.ConnectionString,
                ["ConnectionStrings:gna"] = gnaDatabase.ConnectionString,
                ["ConnectionStrings:rabbitmq"] = rabbit
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSessionsInfrastructure(configuration);
            services.AddGnaInfrastructure(configuration);
            services.AddMessaging(typeof(NotificationMessageHandler).Assembly);
            // No host is started: only the production workers explicitly invoked below execute.
            await using var provider = services.BuildServiceProvider();
            provider.UseMessaging();

            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<EirSessionDbContext>().Database.MigrateAtStartupAsync(token);
                await scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Database.MigrateAtStartupAsync(token);
            }

            var sessionId = Guid.NewGuid();
            Guid messageId;
            NotificationCreatedMessage payload;
            await using (var scope = provider.CreateAsyncScope())
            {
                // Start at the committed outbox boundary. Business-event mapping has separate tests;
                // public session refreshes use SessionChangedMessage and do not create Gna history.
                var outgoing = OutgoingMessage.Event(Guid.NewGuid(), new NotificationCreatedMessage(
                    "teacher-integration", "An answer is available", sessionId, 1, Guid.NewGuid(), "Answered", "Completed",
                    NotificationEventType.HelpRequestAnswered, NotificationDeliveryType.RealtimeOrPush),
                    MessageRoutes.Notifications, MessageRoutes.NotificationCreated, DateTimeOffset.UtcNow);
                scope.ServiceProvider.GetRequiredService<EirSessionDbContext>().Outbox.Add(
                    OutboxMessage.Create(outgoing, DateTimeOffset.UtcNow));
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork<ISessionRepository>>().SaveChangesAsync(token);
            }
            await using (var verification = provider.CreateAsyncScope())
            {
                var db = verification.ServiceProvider.GetRequiredService<EirSessionDbContext>();
                var pending = await db.Outbox.AsNoTracking().SingleAsync(token);
                Assert.Null(pending.PublishedAtUtc);
                messageId = pending.MessageId;
                payload = JsonSerializer.Deserialize<NotificationCreatedMessage>(pending.Payload,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                Assert.Equal(sessionId, payload.SessionId);
            }

            var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var redelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var deliveryCount = 0;
            var bus = provider.GetRequiredService<IMessageBus>();
            queueAttempted = true;
            await using var subscription = await bus.SubscribeAsync<NotificationCreatedMessage>(queue,
                MessageRoutes.Notifications, MessageRoutes.NotificationCreated, async (message, cancellationToken) =>
                {
                    if (message.Id != messageId) return;
                    try
                    {
                        await using var scope = provider.CreateAsyncScope();
                        await scope.ServiceProvider.GetRequiredService<INotificationMessageHandler>()
                            .HandleAsync(message, cancellationToken);
                        if (Interlocked.Increment(ref deliveryCount) == 1) accepted.TrySetResult();
                        else redelivered.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        accepted.TrySetException(exception);
                        redelivered.TrySetException(exception);
                        throw;
                    }
                }, token);

            using var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirSessionDbContext>>(provider);
            await dispatcher.PublishPendingAsync(token);
            await accepted.Task.WaitAsync(token);
            // Model the documented at-least-once transport: the same message id must be idempotent.
            await bus.PublishAsync(messageId, payload, MessageRoutes.Notifications, MessageRoutes.NotificationCreated, token);
            await redelivered.Task.WaitAsync(token);

            await using var check = provider.CreateAsyncScope();
            var eir = check.ServiceProvider.GetRequiredService<EirSessionDbContext>();
            Assert.NotNull((await eir.Outbox.AsNoTracking().SingleAsync(token)).PublishedAtUtc);
            var gna = check.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
            var notification = Assert.Single(await gna.Notifications.AsNoTracking().ToArrayAsync(token));
            Assert.Equal(messageId, notification.Id);
            Assert.Equal("teacher-integration", notification.UserId.Value);
            Assert.Equal(sessionId, notification.SessionId);
            Assert.Equal(NotificationEventType.HelpRequestAnswered, notification.EventType);
            Assert.Equal(NotificationDeliveryType.RealtimeOrPush, notification.DeliveryType);
            var delivery = Assert.Single(await gna.Outbox.AsNoTracking().ToArrayAsync(token));
            Assert.Equal(messageId, delivery.MessageId);
            Assert.Equal(MessageRoutes.NotificationDeliveryRequested, delivery.RoutingKey);
            Assert.Null(delivery.PublishedAtUtc);
            var deliveryPayload = JsonSerializer.Deserialize<NotificationDeliveryRequestedMessage>(delivery.Payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(messageId, deliveryPayload.NotificationId);
            Assert.Equal(sessionId, deliveryPayload.SessionId);
        }
        finally
        {
            // Cleanup has its own deadline, including when the main test timed out.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var errors = new List<Exception>();
            if (queueAttempted)
            {
                try
                {
                    await using var connection = await new ConnectionFactory { Uri = new Uri(rabbit) }.CreateConnectionAsync(cleanup.Token);
                    await using var channel = await connection.CreateChannelAsync(cancellationToken: cleanup.Token);
                    await channel.QueueDeleteAsync(queue, ifUnused: false, ifEmpty: false, cancellationToken: cleanup.Token);
                }
                catch (Exception exception) { errors.Add(exception); }
            }
            if (errors.Count > 0) throw new AggregateException("Could not clean up isolated integration resources.", errors);
        }
    }
}

public sealed class PostgresRabbitMqFactAttribute : FactAttribute
{
    public PostgresRabbitMqFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIFROST_TEST_POSTGRES")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIFROST_TEST_RABBITMQ")))
            Skip = "Set BIFROST_TEST_POSTGRES and BIFROST_TEST_RABBITMQ to isolated test servers.";
    }
}
