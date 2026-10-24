using System.Net;
using System.Net.Http.Json;
using EirService.HelpRequests.Application.Features.Internal.SessionUpdated;
using EirService.Sessions.Application.Features.Teacher.ArchiveSession;
using EirService.Sessions.Infrastructure;
using EirService.Sessions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Infrastructure.Persistence.Outbox;
using MimirSessionUpdatedHandler = MimirService.Application.Services.Messaging.SessionUpdatedMessageHandler;

namespace EirService.Test;

public sealed partial class SessionIntegrationTests
{
    [OutboxBrokerFact]
    public async Task ArchiveWaitsInMimirsDurableQueueUntilMimirStarts()
    {
        var session = await CreateAsync();
        using var response = await ArchiveAsync(session.Id, 1);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var archive = (await response.Content.ReadFromJsonAsync<ArchiveSessionResult>())!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:eir"] = connectionString,
            ["ConnectionStrings:rabbitmq"] = Environment.GetEnvironmentVariable("BIFROST_TEST_RABBITMQ")
        }).Build();
        var registrations = new ServiceCollection().AddLogging();
        registrations.AddSingleton<IConfiguration>(configuration);
        registrations.AddSessionsInfrastructure(configuration);
        await using var producer = registrations.BuildServiceProvider();
        var bus = producer.GetRequiredService<IMessageBus>();
        var factory = new ConnectionFactory { Uri = new Uri(Environment.GetEnvironmentVariable("BIFROST_TEST_RABBITMQ")!) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        const string mimirQueue = "mimir-session-updated";
        var eirQueue = "archive-startup-test-" + Guid.NewGuid().ToString("N");
        await channel.QueueDeleteAsync(mimirQueue);
        try
        {
            // Another bound queue makes mandatory publication succeed even with Mimir offline.
            await using var eir = await bus.SubscribeAsync<SessionUpdatedIntegrationEvent>(eirQueue,
                MessageRoutes.Sessions, MessageRoutes.SessionUpdated, async (message, ct) =>
                {
                    await using var scope = app.Services.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<ISessionUpdatedMessageHandler>().HandleAsync(message, ct);
                });
            var hosted = producer.GetServices<IHostedService>().ToArray();
            var initializer = Array.FindIndex(hosted, service => service.GetType().Name == "RabbitMqQueueInitializer");
            var dispatcherIndex = Array.FindIndex(hosted, service => service.GetType().Name.StartsWith("OutboxDispatcher"));
            Assert.True(initializer >= 0 && initializer < dispatcherIndex);
            await hosted[initializer].StartAsync(CancellationToken.None);
            await ActivatorUtilities.CreateInstance<OutboxDispatcher<EirSessionDbContext>>(producer)
                .PublishPendingAsync(CancellationToken.None);
            await using (var scope = app.Services.CreateAsyncScope())
                Assert.NotNull((await scope.ServiceProvider.GetRequiredService<EirSessionDbContext>().Outbox.SingleAsync()).PublishedAtUtc);

            // Mimir starts after the archive has already been published and receives the retained message.
            var received = new TaskCompletionSource<SessionUpdatedIntegrationEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var mimir = await bus.SubscribeAsync<SessionUpdatedIntegrationEvent>(mimirQueue,
                MessageRoutes.Sessions, MessageRoutes.SessionUpdated, async (message, ct) =>
                {
                    await new MimirSessionUpdatedHandler(NullLogger<MimirSessionUpdatedHandler>.Instance).HandleAsync(message, ct);
                    received.TrySetResult(message.Payload);
                });
            var update = await received.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(SessionUpdateType.Archived, update.EventType);
            Assert.Equal(session.Id, update.SessionId);
            Assert.Equal(archive.Generation, update.PreviousGeneration);
            Assert.Equal(archive.CurrentGeneration, update.Generation);
        }
        finally
        {
            await channel.QueueDeleteAsync(eirQueue);
            await channel.QueueDeleteAsync(mimirQueue);
            await channel.ExchangeDeleteAsync(MessageRoutes.Sessions);
        }
    }
}
