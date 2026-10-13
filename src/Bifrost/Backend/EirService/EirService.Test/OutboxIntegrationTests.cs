using System.Diagnostics;
using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using EirSessionUpdatedMessageHandler = EirService.HelpRequests.Application.Features.Internal.SessionUpdated.SessionUpdatedMessageHandler;
using System.Text.Json;
using EirService.HelpRequests.Domain.Entities;
using EirService.HelpRequests.Infrastructure;
using EirService.HelpRequests.Infrastructure.Persistence;
using EirService.HelpRequests.Application.Repositories;
using GnaService.Application.Services.Messaging;
using GnaService.Infrastructure;
using GnaService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using IMimirHelpRequestRepository = MimirService.Application.Repositories.IHelpRequestRepository;
using MimirService.Application.Services.Messaging;
using MimirHelpRequest = MimirService.Domain.Entities.HelpRequest;
using MimirService.Infrastructure;
using MimirService.Infrastructure.Persistence;
using Npgsql;
using RabbitMQ.Client;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;
using Shared.Infrastructure.Persistence.Outbox;
using Shared.Infrastructure.Persistence.Migrations;
using EirService.Sessions.Infrastructure;
using EirService.Sessions.Infrastructure.Persistence;
using EirService.Participants.Infrastructure;
using EirService.Participants.Infrastructure.Persistence;

namespace EirService.Test;

public sealed class OutboxIntegrationTests : IAsyncLifetime
{
    private string? adminConnectionString;
    private string databaseName = null!;
    private string connectionString = null!;
    private readonly RecordingBus bus = new();
    private readonly SessionUpdateLogger<SessionUpdatedMessageHandler> sessionUpdates = new();
    private readonly SessionUpdateLogger<EirSessionUpdatedMessageHandler> helpRequestSessionUpdates = new();
    private readonly FailPublicationSave interceptor = new();

    public async Task InitializeAsync()
    {
        adminConnectionString = Environment.GetEnvironmentVariable("BIFROST_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(adminConnectionString)) return;
        databaseName = "bifrost_outbox_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin);
        await create.ExecuteNonQueryAsync();
        connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = databaseName }.ConnectionString;
    }

    private ServiceProvider BuildServices(string service, bool realBroker = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{service}"] = connectionString,
            ["ConnectionStrings:rabbitmq"] = realBroker
                ? Environment.GetEnvironmentVariable("BIFROST_TEST_RABBITMQ") : "amqp://unused-in-tests"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILogger<SessionUpdatedMessageHandler>>(sessionUpdates);
        services.AddSingleton<ILogger<EirSessionUpdatedMessageHandler>>(helpRequestSessionUpdates);
        services.AddSingleton<IConfiguration>(configuration);
        switch (service)
        {
            case "eir":
                services.AddHelpRequestsInfrastructure(configuration);
                services.ConfigureDbContext<EirRequestDbContext>(options => options.AddInterceptors(interceptor));
                break;
            case "mimir": services.AddMimirInfrastructure(configuration); break;
            case "gna": services.AddGnaInfrastructure(configuration); break;
        }
        if (!realBroker)
        {
            services.RemoveAll<IMessageBus>();
            services.AddSingleton<IMessageBus>(bus);
        }
        return services.BuildServiceProvider();
    }

    private static OutgoingMessage Reply(Guid id, Guid? sessionId = null, int? generation = null) =>
        new(id, new GeneralizedCreatedMessage("Generalized answer", sessionId, generation),
            MessageRoutes.Requests, MessageRoutes.GeneralizedCreated);

    [PostgresFact]
    public async Task EirStartupMigrationsCreateEachHistoryTableBeforeReadingAndCanRunAgain()
    {
        var errors = new List<string>();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:eir"] = connectionString,
            ["ConnectionStrings:rabbitmq"] = "amqp://unused-in-tests"
        }).Build();
        var registrations = new ServiceCollection();
        registrations.AddLogging();
        registrations.AddHelpRequestsInfrastructure(configuration);
        registrations.AddSessionsInfrastructure(configuration);
        registrations.AddParticipantsInfrastructure(configuration);
        registrations.ConfigureDbContext<EirRequestDbContext>(options => options.LogTo(errors.Add, LogLevel.Error));
        registrations.ConfigureDbContext<EirSessionDbContext>(options => options.LogTo(errors.Add, LogLevel.Error));
        registrations.ConfigureDbContext<EirParticipantDbContext>(options => options.LogTo(errors.Add, LogLevel.Error));
        await using var services = registrations.BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        DbContext[] contexts =
        [
            scope.ServiceProvider.GetRequiredService<EirRequestDbContext>(),
            scope.ServiceProvider.GetRequiredService<EirSessionDbContext>(),
            scope.ServiceProvider.GetRequiredService<EirParticipantDbContext>()
        ];
        foreach (var db in contexts)
        {
            await db.Database.MigrateAtStartupAsync();
            db.Database.MigrateAtStartup();
            Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
            Assert.False(db.Database.HasPendingModelChanges());
        }
        Assert.Empty(errors);
    }

    [PostgresFact]
    public async Task EirSchemaMigrationPreservesRequestsAndPendingOutboxMessages()
    {
        const string previousMigration = "20261011102800_AddMessagingOutbox";
        await using var services = BuildServices("eir");
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(previousMigration);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO public.requests ("Id", "Message", "UserId", "SessionId", "Generation")
            VALUES ('legacy', 'Keep this request', 'student', '11111111-1111-1111-1111-111111111111', 3);
            INSERT INTO public.outbox (
                "Id", "MessageId", "MessageType", "Payload", "ExchangeName", "RoutingKey", "CreatedAtUtc")
            VALUES ('22222222-2222-2222-2222-222222222222', 'legacy', 'HelpRequestCreated',
                '{"message":"Keep this request"}', 'requests', 'request.created', '2026-10-11T10:00:00Z');
            CREATE TABLE public.request_references (
                "RequestId" varchar(100) REFERENCES public.requests ("Id"));
            INSERT INTO public.request_references VALUES ('legacy');
            """, connection))
            await seed.ExecuteNonQueryAsync();

        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();

        var request = Assert.Single(await db.Requests.AsNoTracking().Include(request => request.Messages).ToArrayAsync());
        Assert.Equal(TestIds.From("legacy"), request.Id);
        Assert.Equal("Keep this request", request.Messages.First().Text.Value);
        Assert.Equal("student", request.StudentId);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), request.SessionId);
        Assert.Equal(3, request.Generation);
        var pending = Assert.Single(await db.Outbox.AsNoTracking().ToArrayAsync());
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), pending.Id);
        Assert.Equal(TestIds.From("legacy"), pending.MessageId);
        Assert.Equal("Keep this request", JsonSerializer.Deserialize<JsonElement>(pending.Payload).GetProperty("message").GetString());
        Assert.Null(pending.PublishedAtUtc);
        Assert.False(db.Database.HasPendingModelChanges());

        await using (var tables = new NpgsqlCommand("""
            SELECT to_regclass('helprequests."HelpRequest"') IS NOT NULL
                AND to_regclass('helprequests.outbox') IS NOT NULL
                AND to_regclass('public.requests') IS NULL
                AND to_regclass('public.outbox') IS NULL;
            """, connection))
            Assert.Equal(true, await tables.ExecuteScalarAsync());

        await migrator.MigrateAsync(previousMigration);
        await using (var legacyRows = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM public.requests WHERE "Id" = '0c62a425-29da-ecb7-202d-2f230d2a8d3f') = 1
                AND (SELECT count(*) FROM public.outbox WHERE "MessageId" = '0c62a425-29da-ecb7-202d-2f230d2a8d3f') = 1
                AND (SELECT count(*) FROM public.request_references WHERE "RequestId" = '0c62a425-29da-ecb7-202d-2f230d2a8d3f') = 1
                AND to_regclass('helprequests."HelpRequest"') IS NULL
                AND to_regclass('helprequests.outbox') IS NULL;
            """, connection))
            Assert.Equal(true, await legacyRows.ExecuteScalarAsync());

        await db.Database.MigrateAsync();
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirRequestDbContext>>(services);
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        Assert.Equal(TestIds.From("legacy"), Assert.Single(bus.Messages).Id);
        Assert.NotNull((await db.Outbox.AsNoTracking().SingleAsync()).PublishedAtUtc);
    }

    [PostgresFact]
    public async Task MimirCommitsReplyWithRequestAndIgnoresRedelivery()
    {
        await using var services = BuildServices("mimir");
        var sessionId = Guid.NewGuid();
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MimirRequestDbContext>();
            await db.Database.MigrateAsync();
            var handler = scope.ServiceProvider.GetRequiredService<IHelpRequestMessageHandler>();
            var message = new MessageEnvelope<HelpRequestCreatedMessage>(TestIds.From("request-1"), new("Help me", sessionId, 3));
            await handler.HandleAsync(message, CancellationToken.None);
            await handler.HandleAsync(message, CancellationToken.None);
        }
        await using var verification = services.CreateAsyncScope();
        var saved = verification.ServiceProvider.GetRequiredService<MimirRequestDbContext>();
        Assert.Single(await saved.Requests.ToArrayAsync());
        var outgoing = Assert.Single(await saved.Outbox.ToArrayAsync());
        Assert.Equal(TestIds.From("request-1"), outgoing.MessageId);
        Assert.Equal(typeof(GeneralizedCreatedMessage).FullName, outgoing.MessageType);
        var payload = JsonSerializer.Deserialize<GeneralizedCreatedMessage>(outgoing.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(sessionId, payload.SessionId);
        Assert.Equal(3, payload.Generation);
        Assert.Empty(bus.Messages);
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<MimirRequestDbContext>>(services);
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        Assert.Equal(MessageRoutes.GeneralizedCreated, Assert.Single(bus.Messages).RoutingKey);
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        Assert.Single(bus.Messages);
    }

    [PostgresFact]
    public async Task MimirRollsBackRequestWhenReplyCannotBeStored()
    {
        await using var services = BuildServices("mimir");
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MimirRequestDbContext>();
            await db.Database.MigrateAsync();
            var repository = scope.ServiceProvider.GetRequiredService<IMimirHelpRequestRepository>();
            var reply = Reply(TestIds.From("rollback")) with { RoutingKey = new string('x', 256) };
            await Assert.ThrowsAsync<DbUpdateException>(() => repository.TryAddAsync(MimirHelpRequest.Create(TestIds.From("rollback"), "Help"), reply, CancellationToken.None));
        }
        await using var verification = services.CreateAsyncScope();
        var saved = verification.ServiceProvider.GetRequiredService<MimirRequestDbContext>();
        Assert.Empty(await saved.Requests.ToArrayAsync());
        Assert.Empty(await saved.Outbox.ToArrayAsync());
    }

    [PostgresFact]
    public async Task MimirMigrationPreservesLegacyRequestsAndCanRunAgain()
    {
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var seed = new NpgsqlCommand("""
                CREATE TABLE public.requests (
                    "Id" varchar(100) NOT NULL, "Message" varchar(4000) NOT NULL,
                    "ReceivedAt" timestamptz NOT NULL, CONSTRAINT "PK_requests" PRIMARY KEY ("Id"));
                INSERT INTO public.requests VALUES ('legacy', 'Keep this request', '2026-10-11T10:00:00Z');
                """, connection);
            await seed.ExecuteNonQueryAsync();
        }
        await using var services = BuildServices("mimir");
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MimirRequestDbContext>();
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        Assert.Equal("Keep this request", (await db.Requests.SingleAsync()).Message);
        Assert.Empty(await db.Outbox.ToArrayAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresFact]
    public async Task GnaIgnoresNotificationRedelivery()
    {
        await using var services = BuildServices("gna");
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
            await db.Database.MigrateAsync();
            var handler = scope.ServiceProvider.GetRequiredService<INotificationMessageHandler>();
            var message = new MessageEnvelope<NotificationCreatedMessage>(TestIds.From("notification-1"), new("student-1", "Help needed"));
            await handler.HandleAsync(message, CancellationToken.None);
            await handler.HandleAsync(message, CancellationToken.None);
        }
        await using var verification = services.CreateAsyncScope();
        Assert.Single(await verification.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Notifications.ToArrayAsync());
    }

    [PostgresFact]
    public async Task GenericMessagesRetainTraceAndFailedMessageDoesNotBlockOthers()
    {
        await using var services = BuildServices("eir");
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Bifrost.Messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        ActivityTraceId traceId;
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            await db.Database.MigrateAsync();
            using var activity = new Activity("incoming-request").Start();
            traceId = activity.TraceId;
            var request = TestHelpRequests.Create(TestIds.From("generic"), "student", "Help");
            OutgoingMessage[] messages =
            [
                new(TestIds.From("failed"), new HelpRequestCreatedMessage("Help"), "requests", "fail"),
                new(TestIds.From("custom"), new { Value = "Any payload" }, "custom-exchange", "custom")
            ];
            await TestHelpRequests.SaveWithOutboxAsync(db, request, messages);
        }
        bus.FailRoutingKey = "fail";
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirRequestDbContext>>(services);
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        var delivered = Assert.Single(bus.Messages);
        Assert.Equal(TestIds.From("custom"), delivered.Id);
        Assert.Equal("Any payload", delivered.Payload.GetProperty("value").GetString());
        Assert.Equal(traceId, delivered.TraceId);
        await using (var scope = services.CreateAsyncScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox.CountAsync(message => message.PublishedAtUtc == null));
        bus.FailRoutingKey = null;
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        Assert.Equal(2, bus.Messages.Count);
    }

    [PostgresFact]
    public async Task FailedFirstBatchDoesNotStarveNewMessages()
    {
        await using var services = BuildServices("eir");
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            await db.Database.MigrateAsync();
            var oldTime = DateTimeOffset.UtcNow.AddHours(-1);
            for (var i = 0; i < 50; i++)
                db.Outbox.Add(OutboxMessage.Create(new OutgoingMessage(TestIds.From($"failed-{i}"), new { Value = i }, "requests", "fail"), oldTime));
            db.Outbox.Add(OutboxMessage.Create(new OutgoingMessage(TestIds.From("new"), new { Value = "Deliver me" }, "requests", "ready"), DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        bus.FailRoutingKey = "fail";
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirRequestDbContext>>(services);
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        Assert.Empty(bus.Messages);
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        Assert.Equal(TestIds.From("new"), Assert.Single(bus.Messages).Id);
    }

    [PostgresFact]
    public async Task FailureAfterPublishRetriesWithSameMessageId()
    {
        await using var services = BuildServices("eir");
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            await db.Database.MigrateAsync();
            OutgoingMessage[] messages = [new(TestIds.From("stable-id"), new HelpRequestCreatedMessage("Help"), MessageRoutes.Requests, MessageRoutes.HelpRequestCreated)];
            await TestHelpRequests.SaveWithOutboxAsync(db, TestHelpRequests.Create(TestIds.From("stable-id"), "student", "Help"), messages);
        }
        interceptor.FailNext = true;
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirRequestDbContext>>(services);
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        await using (var scope = services.CreateAsyncScope())
            Assert.Null((await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox.SingleAsync()).PublishedAtUtc);
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        Assert.Equal(new[] { TestIds.From("stable-id"), TestIds.From("stable-id") }, bus.Messages.Select(message => message.Id));
        await using var verification = services.CreateAsyncScope();
        Assert.NotNull((await verification.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox.SingleAsync()).PublishedAtUtc);
    }

    [OutboxBrokerFact]
    public async Task PreparedHelpRequestQueuesAllowOutboxRetryAndKeepEventsForFutureConsumers()
    {
        await using var eir = BuildServices("eir", realBroker: true);
        await using var gna = BuildServices("gna", realBroker: true);
        await using var mimir = BuildServices("mimir", realBroker: true);
        var requestId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var updated = new HelpRequestUpdatedIntegrationEvent(Guid.NewGuid(), requestId, sessionId, 2,
            "student", "Answered", "Completed", "Digital", "teacher", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var answered = new HelpRequestAnsweredMessage(requestId, sessionId, 2, "Question", "Answer");
        await using (var scope = eir.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            await db.Database.MigrateAtStartupAsync();
            db.Outbox.AddRange(
                OutboxMessage.Create(new(updated.EventId, updated, MessageRoutes.Requests, MessageRoutes.HelpRequestUpdated), DateTimeOffset.UtcNow),
                OutboxMessage.Create(new(Guid.NewGuid(), answered, MessageRoutes.Requests, MessageRoutes.HelpRequestAnswered), DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirRequestDbContext>>(eir);
        var factory = new ConnectionFactory { Uri = new Uri(Environment.GetEnvironmentVariable("BIFROST_TEST_RABBITMQ")!) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        try
        {
            // Reproduce the missing-route error; pending events must not be discarded.
            await dispatcher.PublishPendingAsync(CancellationToken.None);
            await using (var scope = eir.CreateAsyncScope())
                Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>()
                    .Outbox.CountAsync(message => message.PublishedAtUtc == null));

            foreach (var services in new[] { gna, mimir })
            {
                var initializer = services.GetServices<IHostedService>()
                    .Single(service => service.GetType().Name == "RabbitMqQueueInitializer");
                await initializer.StartAsync(CancellationToken.None);
                await initializer.StartAsync(CancellationToken.None); // Safe on every service startup.
                await initializer.StopAsync(CancellationToken.None);
            }
            await dispatcher.PublishPendingAsync(CancellationToken.None);
            await using (var scope = eir.CreateAsyncScope())
                Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>()
                    .Outbox.CountAsync(message => message.PublishedAtUtc != null));

            foreach (var queueName in new[] { "gna-request-updated", "mimir-request-answered" })
            {
                var queue = await channel.QueueDeclarePassiveAsync(queueName);
                Assert.Equal(1u, queue.MessageCount);
                Assert.Equal(0u, queue.ConsumerCount);
            }
            var storedUpdate = await channel.BasicGetAsync("gna-request-updated", autoAck: false);
            var storedAnswer = await channel.BasicGetAsync("mimir-request-answered", autoAck: false);
            Assert.NotNull(storedUpdate);
            Assert.NotNull(storedAnswer);
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            Assert.Equal(updated, JsonSerializer.Deserialize<MessageEnvelope<HelpRequestUpdatedIntegrationEvent>>(storedUpdate.Body.Span, options)!.Payload);
            Assert.Equal(answered, JsonSerializer.Deserialize<MessageEnvelope<HelpRequestAnsweredMessage>>(storedAnswer.Body.Span, options)!.Payload);
            Assert.Equal(DeliveryModes.Persistent, storedUpdate.BasicProperties.DeliveryMode);
            Assert.Equal(DeliveryModes.Persistent, storedAnswer.BasicProperties.DeliveryMode);
            await channel.BasicNackAsync(storedUpdate.DeliveryTag, multiple: false, requeue: true);
            await channel.BasicNackAsync(storedAnswer.DeliveryTag, multiple: false, requeue: true);
        }
        finally
        {
            // These tests require an isolated broker; only the two queues created here are removed.
            await channel.QueueDeleteAsync("gna-request-updated");
            await channel.QueueDeleteAsync("mimir-request-answered");
        }
    }

    [OutboxBrokerFact]
    public async Task RabbitMqRetriesUntilQueuesExistAndDeliversIntegrationContracts()
    {
        await using var services = BuildServices("eir", realBroker: true);
        var exchange = "outbox-test-" + Guid.NewGuid().ToString("N");
        var sessionId = Guid.NewGuid();
        var clearedEvent = new SessionUpdatedIntegrationEvent(Guid.NewGuid(), sessionId, 4, SessionUpdateType.Cleared, DateTimeOffset.UtcNow, 3);
        var messageBus = services.GetRequiredService<IMessageBus>();
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirRequestDbContext>>(services);
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            await db.Database.MigrateAsync();
            OutgoingMessage[] messages =
            [
                new(TestIds.From("request"), new HelpRequestCreatedMessage("Help", sessionId, 4), exchange, MessageRoutes.HelpRequestCreated),
                new(TestIds.From("request"), new NotificationCreatedMessage("student", "Help", sessionId, 4), exchange, MessageRoutes.NotificationCreated),
                new(TestIds.From("request"), new GeneralizedCreatedMessage("Answer", sessionId, 4), exchange, MessageRoutes.GeneralizedCreated),
                new(clearedEvent.EventId, clearedEvent, exchange, MessageRoutes.SessionUpdated)
            ];
            await TestHelpRequests.SaveWithOutboxAsync(db, TestHelpRequests.Create(TestIds.From("request"), "student", "Help"), messages);
        }
        // Publisher confirmations alone do not reject messages without a bound queue. Mandatory publishing must keep them pending.
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        await using (var scope = services.CreateAsyncScope())
            Assert.Equal(4, await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox.CountAsync(message => message.PublishedAtUtc == null));
        var help = new TaskCompletionSource<MessageEnvelope<HelpRequestCreatedMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notification = new TaskCompletionSource<MessageEnvelope<NotificationCreatedMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var generalized = new TaskCompletionSource<MessageEnvelope<GeneralizedCreatedMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleared = new TaskCompletionSource<MessageEnvelope<SessionUpdatedIntegrationEvent>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var helpSubscription = await messageBus.SubscribeAsync<HelpRequestCreatedMessage>(exchange + "-help", exchange, MessageRoutes.HelpRequestCreated,
            (message, _) => { help.TrySetResult(message); return Task.CompletedTask; });
        await using var notificationSubscription = await messageBus.SubscribeAsync<NotificationCreatedMessage>(exchange + "-notification", exchange, MessageRoutes.NotificationCreated,
            (message, _) => { notification.TrySetResult(message); return Task.CompletedTask; });
        await using var generalizedSubscription = await messageBus.SubscribeAsync<GeneralizedCreatedMessage>(exchange + "-generalized", exchange, MessageRoutes.GeneralizedCreated,
            (message, _) => { generalized.TrySetResult(message); return Task.CompletedTask; });
        await using var clearedSubscription = await messageBus.SubscribeAsync<SessionUpdatedIntegrationEvent>(exchange + "-cleared", exchange, MessageRoutes.SessionUpdated,
            (message, _) => { cleared.TrySetResult(message); return Task.CompletedTask; });
        try
        {
            await dispatcher.PublishPendingAsync(CancellationToken.None);
            Assert.Equal(new HelpRequestCreatedMessage("Help", sessionId, 4), (await help.Task.WaitAsync(TimeSpan.FromSeconds(15))).Payload);
            Assert.Equal(new NotificationCreatedMessage("student", "Help", sessionId, 4), (await notification.Task.WaitAsync(TimeSpan.FromSeconds(15))).Payload);
            Assert.Equal(new GeneralizedCreatedMessage("Answer", sessionId, 4), (await generalized.Task.WaitAsync(TimeSpan.FromSeconds(15))).Payload);
            Assert.Equal(TestIds.From("request"), help.Task.Result.Id);
            Assert.Equal(clearedEvent, (await cleared.Task.WaitAsync(TimeSpan.FromSeconds(15))).Payload);
            Assert.Equal(clearedEvent.EventId, cleared.Task.Result.Id);
            await using var scope = services.CreateAsyncScope();
            Assert.Equal(4, await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox.CountAsync(message => message.PublishedAtUtc != null));
        }
        finally
        {
            var factory = new ConnectionFactory { Uri = new Uri(Environment.GetEnvironmentVariable("BIFROST_TEST_RABBITMQ")!) };
            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
            foreach (var suffix in new[] { "-help", "-notification", "-generalized", "-cleared" })
                await channel.QueueDeleteAsync(exchange + suffix);
            await channel.ExchangeDeleteAsync(exchange);
        }
    }

    [PostgresFact]
    public async Task GnaAdoptsLegacyNotificationsAndPreservesGuidAndOpaqueIds()
    {
        var existingId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var seed = new NpgsqlCommand("""
                CREATE TABLE public.notifications (
                    "Id" varchar(100) PRIMARY KEY, "UserId" varchar(100) NOT NULL,
                    "Messages" varchar(4000) NOT NULL, "CreatedAt" timestamptz NOT NULL);
                INSERT INTO public.notifications VALUES ('legacy', 'provider|student', 'Preserve me', '2026-10-11T10:00:00Z');
                INSERT INTO public.notifications VALUES (@id, 'provider|student', 'Keep my GUID', '2026-10-11T10:00:00Z');
                """, connection);
            seed.Parameters.AddWithValue("id", existingId.ToString());
            await seed.ExecuteNonQueryAsync();
        }
        await using var services = BuildServices("gna");
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        var notifications = await db.Notifications.AsNoTracking().ToArrayAsync();
        Assert.Equal(2, notifications.Length);
        Assert.Contains(notifications, notification => notification.Id == TestIds.From("legacy") && notification.Messages.Value == "Preserve me");
        Assert.Contains(notifications, notification => notification.Id == existingId && notification.Messages.Value == "Keep my GUID");
        Assert.All(notifications, notification => Assert.Equal("provider|student", notification.UserId.Value));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [OutboxBrokerFact]
    public async Task MimirSubscriberReceivesAllSessionUpdateTypesFromTheOutbox()
    {
        await using var services = BuildServices("mimir", realBroker: true);
        var sessionId = Guid.NewGuid();
        var updates = Enum.GetValues<SessionUpdateType>().Select(type =>
            new SessionUpdatedIntegrationEvent(Guid.NewGuid(), sessionId, 4, type, DateTimeOffset.UtcNow, 3)).ToArray();
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MimirRequestDbContext>();
            await db.Database.MigrateAsync();
            db.Outbox.AddRange(updates.Select(update => OutboxMessage.Create(
                new(update.EventId, update, MessageRoutes.Sessions, MessageRoutes.SessionUpdated), DateTimeOffset.UtcNow)));
            await db.SaveChangesAsync();
        }
        var subscriber = services.GetServices<IHostedService>().Single(service => service.GetType().Name == "SessionUpdatedMessageSubscriber");
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<MimirRequestDbContext>>(services);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await subscriber.StartAsync(deadline.Token);
        try
        {
            // Publishing before the queue is bound keeps messages pending; retry until the real subscriber receives them.
            while (sessionUpdates.Messages.Count < 3)
            {
                await dispatcher.PublishPendingAsync(deadline.Token);
                await Task.Delay(100, deadline.Token);
            }
            foreach (var update in updates)
                Assert.Contains(sessionUpdates.Messages, logged => logged.Contains(update.EventType.ToString())
                    && logged.Contains(sessionId.ToString()) && logged.Contains(update.EventId.ToString()));
            await using var scope = services.CreateAsyncScope();
            Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<MimirRequestDbContext>().Outbox.CountAsync(message => message.PublishedAtUtc != null));
        }
        finally
        {
            await subscriber.StopAsync(CancellationToken.None);
            var factory = new ConnectionFactory { Uri = new Uri(Environment.GetEnvironmentVariable("BIFROST_TEST_RABBITMQ")!) };
            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
            await channel.QueueDeleteAsync("mimir-session-updated");
            await channel.ExchangeDeleteAsync(MessageRoutes.Sessions);
        }
    }

    [OutboxBrokerFact]
    public async Task HelpRequestsSubscriberLogsDeletedSessionAndIgnoresOtherUpdates()
    {
        await using var services = BuildServices("eir", realBroker: true);
        var sessionId = Guid.NewGuid();
        var updates = Enum.GetValues<SessionUpdateType>().Select(type =>
            new SessionUpdatedIntegrationEvent(Guid.NewGuid(), sessionId, 4, type, DateTimeOffset.UtcNow)).ToArray();
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            await db.Database.MigrateAtStartupAsync();
            db.Outbox.AddRange(updates.Select(update => OutboxMessage.Create(
                new(update.EventId, update, MessageRoutes.Sessions, MessageRoutes.SessionUpdated), DateTimeOffset.UtcNow)));
            await db.SaveChangesAsync();
        }
        var subscriber = services.GetServices<IHostedService>().Single(service => service.GetType().Name == "SessionUpdatedMessageSubscriber");
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirRequestDbContext>>(services);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await subscriber.StartAsync(deadline.Token);
        try
        {
            while (helpRequestSessionUpdates.Messages.IsEmpty)
            {
                await dispatcher.PublishPendingAsync(deadline.Token);
                await Task.Delay(100, deadline.Token);
            }
            // The Deleted message proves the subscriber is bound. Retry any earlier update published before binding.
            await dispatcher.PublishPendingAsync(deadline.Token);
            var logged = Assert.Single(helpRequestSessionUpdates.Messages);
            Assert.Contains(sessionId.ToString(), logged);
            Assert.Contains(updates.Single(update => update.EventType == SessionUpdateType.Deleted).EventId.ToString(), logged);
            Assert.Contains("all session messages across all generations", logged);
            await using var scope = services.CreateAsyncScope();
            Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox.CountAsync(message => message.PublishedAtUtc != null));
        }
        finally
        {
            await subscriber.StopAsync(CancellationToken.None);
            var factory = new ConnectionFactory { Uri = new Uri(Environment.GetEnvironmentVariable("BIFROST_TEST_RABBITMQ")!) };
            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
            await channel.QueueDeleteAsync("eir-helprequests-session-updated");
            await channel.ExchangeDeleteAsync(MessageRoutes.Sessions);
        }
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(adminConnectionString)) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    private sealed class SessionUpdateLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Enqueue(formatter(state, exception));
    }

    private sealed class RecordingBus : IMessageBus
    {
        public string? FailRoutingKey { get; set; }
        public List<(Guid Id, JsonElement Payload, string RoutingKey, ActivityTraceId? TraceId)> Messages { get; } = [];
        public Task PublishAsync(Guid id, object payload, string exchangeName, string routingKey,
            CancellationToken cancellationToken = default, string? messageType = null)
        {
            if (routingKey == FailRoutingKey) throw new InvalidOperationException("Simulated broker failure");
            Messages.Add((id, (JsonElement)payload, routingKey, Activity.Current?.TraceId));
            return Task.CompletedTask;
        }
        public Task<IAsyncDisposable> SubscribeAsync<T>(string queueName, string exchangeName, string routingKey,
            Func<MessageEnvelope<T>, CancellationToken, Task> handler, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FailPublicationSave : SaveChangesInterceptor
    {
        public bool FailNext { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (FailNext && eventData.Context!.ChangeTracker.Entries<OutboxMessage>().Any(entry => entry.Entity.PublishedAtUtc != null))
            {
                FailNext = false;
                throw new InvalidOperationException("Simulated failure after broker confirmation");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
