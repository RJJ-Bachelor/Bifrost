using System.Diagnostics;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using EirService.Sessions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Outbox;
using Shared.Infrastructure.Persistence.Outbox;

namespace Backend.IntegrationTests.Persistence;

public sealed class OutboxDispatcherTests
{
    [Fact]
    public async Task FailedPublicationLeavesRowPendingAndDoesNotBlockOtherMessagesOrRetryDuplicates()
    {
        await using var database = new SqliteTestDatabase();
        var bus = new RecordingBus();
        await using var provider = BuildServices(database, bus);
        var failed = Guid.NewGuid();
        var successful = Guid.NewGuid();
        bus.FailFor.Add(failed);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Outbox.AddRange(Stage(failed), Stage(successful));
            await db.SaveChangesAsync();
        }

        using var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirSessionDbContext>>(provider);
        using var parent = new Activity("test parent").Start();
        await dispatcher.PublishPendingAsync(default);
        Assert.Same(parent, Activity.Current);
        Assert.Equal(successful, Assert.Single(bus.Published).Id);
        Assert.Equal("notification.created", bus.Published[0].RoutingKey);
        Assert.Equal("payload", ((JsonElement)bus.Published[0].Payload).GetProperty("text").GetString());
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
            var pending = await db.Outbox.SingleAsync(row => row.MessageId == failed);
            Assert.Null(pending.PublishedAtUtc);
            Assert.NotNull(pending.LastAttemptAtUtc);
            Assert.NotNull((await db.Outbox.SingleAsync(row => row.MessageId == successful)).PublishedAtUtc);
        }
        bus.FailFor.Clear();
        await dispatcher.PublishPendingAsync(default);
        await dispatcher.PublishPendingAsync(default);
        Assert.Equal(2, bus.Published.Count);
        Assert.Equal(failed, bus.Published.Last().Id);
        await using var verification = provider.CreateAsyncScope();
        Assert.All(await verification.ServiceProvider.GetRequiredService<EirSessionDbContext>().Outbox.ToArrayAsync(), row => Assert.NotNull(row.PublishedAtUtc));
    }

    [Fact]
    public async Task DispatcherFetchesAtMostFiftyPendingMessagesPerCall()
    {
        await using var database = new SqliteTestDatabase();
        var bus = new RecordingBus();
        await using var provider = BuildServices(database, bus);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Outbox.AddRange(Enumerable.Range(0, 51).Select(_ => Stage(Guid.NewGuid())));
            await db.SaveChangesAsync();
        }
        using var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirSessionDbContext>>(provider);
        await dispatcher.PublishPendingAsync(default);
        Assert.Equal(50, bus.Published.Count);
        await dispatcher.PublishPendingAsync(default);
        Assert.Equal(51, bus.Published.Count);
        Assert.Equal(51, bus.Published.Select(message => message.Id).Distinct().Count());
    }

    [Fact]
    public async Task CancelledPublishLeavesMessagePendingWithoutMarkingAFailure()
    {
        await using var database = new SqliteTestDatabase();
        using var cancellation = new CancellationTokenSource();
        var bus = new RecordingBus { CancelOnPublish = cancellation };
        await using var provider = BuildServices(database, bus);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Outbox.Add(Stage(Guid.NewGuid()));
            await db.SaveChangesAsync();
        }
        using var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirSessionDbContext>>(provider);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.PublishPendingAsync(cancellation.Token));
        await using var verification = provider.CreateAsyncScope();
        var message = await verification.ServiceProvider.GetRequiredService<EirSessionDbContext>().Outbox.SingleAsync();
        Assert.Null(message.PublishedAtUtc);
        Assert.Null(message.LastAttemptAtUtc);
    }

    [Fact]
    public async Task HostedWorkerDrainsPendingMessagesBeforeWaitingForItsNextPoll()
    {
        await using var database = new SqliteTestDatabase();
        var clock = new OutboxPollingTimeProvider();
        var bus = new RecordingBus();
        await using var provider = BuildServices(database, bus, clock);
        await SeedAsync(provider, Enumerable.Range(0, 151).Select(_ => Stage(Guid.NewGuid())));
        using var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirSessionDbContext>>(
            provider, TimeSpan.FromHours(1));

        await dispatcher.StartAsync(default);
        try
        {
            await clock.WaitingForPoll.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(151, bus.Published.Count);
        }
        finally { await dispatcher.StopAsync(default); }

        await using var verification = provider.CreateAsyncScope();
        Assert.Equal(151, await verification.ServiceProvider.GetRequiredService<EirSessionDbContext>().Outbox
            .CountAsync(row => row.PublishedAtUtc != null));
    }

    [Fact]
    public async Task PublicationFailuresWaitForTheNextPollRatherThanRetryingInABusyLoop()
    {
        await using var database = new SqliteTestDatabase();
        var clock = new OutboxPollingTimeProvider();
        var bus = new RecordingBus { FailEveryPublish = true };
        await using var provider = BuildServices(database, bus, clock);
        await SeedAsync(provider, Enumerable.Range(0, 51).Select(_ => Stage(Guid.NewGuid())));
        using var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirSessionDbContext>>(
            provider, TimeSpan.FromHours(1));

        await dispatcher.StartAsync(default);
        try
        {
            await clock.WaitingForPoll.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(50, bus.Attempts);
        }
        finally { await dispatcher.StopAsync(default); }
        Assert.Equal(50, bus.Attempts);
        await using var verification = provider.CreateAsyncScope();
        var rows = await verification.ServiceProvider.GetRequiredService<EirSessionDbContext>().Outbox.ToArrayAsync();
        Assert.All(rows, row => Assert.Null(row.PublishedAtUtc));
        Assert.Equal(50, rows.Count(row => row.LastAttemptAtUtc != null));
    }

    [Fact]
    public async Task CancellationPreservesEarlierConfirmedMessages()
    {
        await using var database = new SqliteTestDatabase();
        using var cancellation = new CancellationTokenSource();
        var bus = new RecordingBus { CancelOnPublish = cancellation, PublishedBeforeCancellation = 1 };
        await using var provider = BuildServices(database, bus);
        var first = Stage(Guid.NewGuid());
        var second = OutboxMessage.Create(new OutgoingMessage(Guid.NewGuid(), new { Text = "second" },
            "notifications", "notification.created"), first.CreatedAtUtc.AddSeconds(1));
        await SeedAsync(provider, [first, second]);
        using var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirSessionDbContext>>(provider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.PublishPendingAsync(cancellation.Token));

        Assert.Equal(first.MessageId, Assert.Single(bus.Published).Id);
        await using var verification = provider.CreateAsyncScope();
        var rows = await verification.ServiceProvider.GetRequiredService<EirSessionDbContext>().Outbox.ToArrayAsync();
        Assert.NotNull(rows.Single(row => row.Id == first.Id).PublishedAtUtc);
        var pending = rows.Single(row => row.Id == second.Id);
        Assert.Null(pending.PublishedAtUtc);
        Assert.Null(pending.LastAttemptAtUtc);
    }

    [Fact]
    public async Task FailedRowsMoveBehindUnattemptedMessages()
    {
        await using var database = new SqliteTestDatabase();
        var bus = new RecordingBus();
        await using var provider = BuildServices(database, bus);
        var now = DateTimeOffset.UtcNow;
        var failed = Enumerable.Range(0, 50).Select(_ => OutboxMessage.Create(
            new OutgoingMessage(Guid.NewGuid(), new { Text = "failed" }, "notifications", "notification.created"),
            now.AddMinutes(-1))).ToArray();
        bus.FailFor.UnionWith(failed.Select(row => row.MessageId));
        var successful = OutboxMessage.Create(
            new OutgoingMessage(Guid.NewGuid(), new { Text = "successful" }, "notifications", "notification.created"),
            now.AddSeconds(-30));
        await SeedAsync(provider, failed.Append(successful));
        using var dispatcher = ActivatorUtilities.CreateInstance<OutboxDispatcher<EirSessionDbContext>>(provider);

        await dispatcher.PublishPendingAsync(default);
        Assert.Empty(bus.Published);
        await dispatcher.PublishPendingAsync(default);

        Assert.Equal(successful.MessageId, Assert.Single(bus.Published).Id);
        await using var verification = provider.CreateAsyncScope();
        var rows = await verification.ServiceProvider.GetRequiredService<EirSessionDbContext>().Outbox.ToArrayAsync();
        Assert.NotNull(rows.Single(row => row.Id == successful.Id).PublishedAtUtc);
        Assert.All(rows.Where(row => row.Id != successful.Id), row => Assert.Null(row.PublishedAtUtc));
    }

    private static async Task SeedAsync(ServiceProvider provider, IEnumerable<OutboxMessage> rows)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EirSessionDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Outbox.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static OutboxMessage Stage(Guid id) => OutboxMessage.Create(
        new OutgoingMessage(id, new { Text = "payload" }, "notifications", "notification.created"), DateTimeOffset.UtcNow);

    private static ServiceProvider BuildServices(SqliteTestDatabase database, RecordingBus bus,
        TimeProvider? clock = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock ?? TimeProvider.System);
        services.AddSingleton<IMessageBus>(bus);
        database.Register<EirSessionDbContext>(services);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingBus : IMessageBus
    {
        public HashSet<Guid> FailFor { get; } = [];
        public List<(Guid Id, object Payload, string RoutingKey)> Published { get; } = [];
        public CancellationTokenSource? CancelOnPublish { get; init; }
        public int PublishedBeforeCancellation { get; init; }
        public bool FailEveryPublish { get; init; }
        public int Attempts { get; private set; }

        public Task PublishAsync(Guid id, object payload, string exchangeName, string routingKey,
            CancellationToken cancellationToken = default, string? messageType = null)
        {
            Attempts++;
            if (Published.Count >= PublishedBeforeCancellation) CancelOnPublish?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            if (FailEveryPublish || FailFor.Contains(id)) throw new IOException("Broker unavailable");
            Published.Add((id, payload, routingKey));
            return Task.CompletedTask;
        }

        public Task<IAsyncDisposable> SubscribeAsync<T>(string queueName, string exchangeName, string routingKey,
            Func<MessageEnvelope<T>, CancellationToken, Task> handler, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This fixture observes outbox publications only.");
    }

}
