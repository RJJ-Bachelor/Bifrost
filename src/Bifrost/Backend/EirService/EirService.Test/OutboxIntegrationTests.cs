using System.Diagnostics;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MimirService.Application.Repositories;
using MimirService.Application.Services.Messaging;
using MimirService.Domain.Entities;
using MimirService.Infrastructure;
using MimirService.Infrastructure.Persistence;
using Npgsql;
using RabbitMQ.Client;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;
using Shared.Infrastructure.Persistence.Outbox;

namespace EirService.Test;

public sealed class OutboxIntegrationTests : IAsyncLifetime
{
    private string? adminConnectionString;
    private string databaseName = null!;
    private string connectionString = null!;
    private readonly RecordingBus bus = new();
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

    private static OutgoingMessage Reply(string id, Guid? sessionId = null, int? generation = null) =>
        new(id, new GeneralizedCreatedMessage("Generalized answer", sessionId, generation),
            MessageRoutes.Requests, MessageRoutes.GeneralizedCreated);

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
            var message = new MessageEnvelope<HelpRequestCreatedMessage>("request-1", new("Help me", sessionId, 3));
            await handler.HandleAsync(message, CancellationToken.None);
            await handler.HandleAsync(message, CancellationToken.None);
        }
        await using var verification = services.CreateAsyncScope();
        var saved = verification.ServiceProvider.GetRequiredService<MimirRequestDbContext>();
        Assert.Single(await saved.Requests.ToArrayAsync());
        var outgoing = Assert.Single(await saved.Outbox.ToArrayAsync());
        Assert.Equal("request-1", outgoing.MessageId);
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
            var repository = scope.ServiceProvider.GetRequiredService<IMimirRequestRepository>();
            var reply = Reply("rollback") with { RoutingKey = new string('x', 256) };
            await Assert.ThrowsAsync<DbUpdateException>(() => repository.TryAddAsync(MimirRequest.Create("rollback", "Help"), reply, CancellationToken.None));
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
            await db.Database.EnsureCreatedAsync();
            var handler = scope.ServiceProvider.GetRequiredService<INotificationMessageHandler>();
            var message = new MessageEnvelope<NotificationCreatedMessage>("notification-1", new("student-1", "Help needed"));
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
            var request = new EirRequest("generic", "student", "Help");
            OutgoingMessage[] messages =
            [
                new("failed", new HelpRequestCreatedMessage("Help"), "requests", "fail"),
                new("custom", new { Value = "Any payload" }, "custom-exchange", "custom")
            ];
            await scope.ServiceProvider.GetRequiredService<IRequestRepository>().AddAsync(request, messages, CancellationToken.None);
        }
        bus.FailRoutingKey = "fail";
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirRequestDbContext>>(services);
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        var delivered = Assert.Single(bus.Messages);
        Assert.Equal("custom", delivered.Id);
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
                db.Outbox.Add(OutboxMessage.Create(new OutgoingMessage($"failed-{i}", new { Value = i }, "requests", "fail"), oldTime));
            db.Outbox.Add(OutboxMessage.Create(new OutgoingMessage("new", new { Value = "Deliver me" }, "requests", "ready"), DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        bus.FailRoutingKey = "fail";
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirRequestDbContext>>(services);
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        Assert.Empty(bus.Messages);
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        Assert.Equal("new", Assert.Single(bus.Messages).Id);
    }

    [PostgresFact]
    public async Task FailureAfterPublishRetriesWithSameMessageId()
    {
        await using var services = BuildServices("eir");
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            await db.Database.MigrateAsync();
            OutgoingMessage[] messages = [new("stable-id", new HelpRequestCreatedMessage("Help"), MessageRoutes.Requests, MessageRoutes.HelpRequestCreated)];
            await scope.ServiceProvider.GetRequiredService<IRequestRepository>().AddAsync(new EirRequest("stable-id", "student", "Help"), messages, CancellationToken.None);
        }
        interceptor.FailNext = true;
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirRequestDbContext>>(services);
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        await using (var scope = services.CreateAsyncScope())
            Assert.Null((await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox.SingleAsync()).PublishedAtUtc);
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        Assert.Equal(new[] { "stable-id", "stable-id" }, bus.Messages.Select(message => message.Id));
        await using var verification = services.CreateAsyncScope();
        Assert.NotNull((await verification.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox.SingleAsync()).PublishedAtUtc);
    }

    [OutboxBrokerFact]
    public async Task RabbitMqRetriesUntilQueuesExistAndDeliversAllThreeContracts()
    {
        await using var services = BuildServices("eir", realBroker: true);
        var exchange = "outbox-test-" + Guid.NewGuid().ToString("N");
        var sessionId = Guid.NewGuid();
        var messageBus = services.GetRequiredService<IMessageBus>();
        var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirRequestDbContext>>(services);
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirRequestDbContext>();
            await db.Database.MigrateAsync();
            OutgoingMessage[] messages =
            [
                new("request", new HelpRequestCreatedMessage("Help", sessionId, 4), exchange, MessageRoutes.HelpRequestCreated),
                new("request", new NotificationCreatedMessage("student", "Help", sessionId, 4), exchange, MessageRoutes.NotificationCreated),
                new("request", new GeneralizedCreatedMessage("Answer", sessionId, 4), exchange, MessageRoutes.GeneralizedCreated)
            ];
            await scope.ServiceProvider.GetRequiredService<IRequestRepository>().AddAsync(new EirRequest("request", "student", "Help"), messages, CancellationToken.None);
        }
        // Publisher confirmations alone do not reject messages without a bound queue. Mandatory publishing must keep them pending.
        await dispatcher.PublishPendingAsync(CancellationToken.None);
        await using (var scope = services.CreateAsyncScope())
            Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox.CountAsync(message => message.PublishedAtUtc == null));
        var help = new TaskCompletionSource<MessageEnvelope<HelpRequestCreatedMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notification = new TaskCompletionSource<MessageEnvelope<NotificationCreatedMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var generalized = new TaskCompletionSource<MessageEnvelope<GeneralizedCreatedMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var helpSubscription = await messageBus.SubscribeAsync<HelpRequestCreatedMessage>(exchange + "-help", exchange, MessageRoutes.HelpRequestCreated,
            (message, _) => { help.TrySetResult(message); return Task.CompletedTask; });
        await using var notificationSubscription = await messageBus.SubscribeAsync<NotificationCreatedMessage>(exchange + "-notification", exchange, MessageRoutes.NotificationCreated,
            (message, _) => { notification.TrySetResult(message); return Task.CompletedTask; });
        await using var generalizedSubscription = await messageBus.SubscribeAsync<GeneralizedCreatedMessage>(exchange + "-generalized", exchange, MessageRoutes.GeneralizedCreated,
            (message, _) => { generalized.TrySetResult(message); return Task.CompletedTask; });
        try
        {
            await dispatcher.PublishPendingAsync(CancellationToken.None);
            Assert.Equal(new HelpRequestCreatedMessage("Help", sessionId, 4), (await help.Task.WaitAsync(TimeSpan.FromSeconds(15))).Payload);
            Assert.Equal(new NotificationCreatedMessage("student", "Help", sessionId, 4), (await notification.Task.WaitAsync(TimeSpan.FromSeconds(15))).Payload);
            Assert.Equal(new GeneralizedCreatedMessage("Answer", sessionId, 4), (await generalized.Task.WaitAsync(TimeSpan.FromSeconds(15))).Payload);
            Assert.Equal("request", help.Task.Result.Id);
            await using var scope = services.CreateAsyncScope();
            Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<EirRequestDbContext>().Outbox.CountAsync(message => message.PublishedAtUtc != null));
        }
        finally
        {
            var factory = new ConnectionFactory { Uri = new Uri(Environment.GetEnvironmentVariable("BIFROST_TEST_RABBITMQ")!) };
            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
            foreach (var suffix in new[] { "-help", "-notification", "-generalized" })
                await channel.QueueDeleteAsync(exchange + suffix);
            await channel.ExchangeDeleteAsync(exchange);
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

    private sealed class RecordingBus : IMessageBus
    {
        public string? FailRoutingKey { get; set; }
        public List<(string Id, JsonElement Payload, string RoutingKey, ActivityTraceId? TraceId)> Messages { get; } = [];
        public Task PublishAsync(string id, object payload, string exchangeName, string routingKey,
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
