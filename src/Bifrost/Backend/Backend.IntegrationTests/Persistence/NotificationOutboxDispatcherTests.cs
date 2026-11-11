using System.Collections.Concurrent;
using Backend.IntegrationTests.Infrastructure;
using GnaService.Application.Notifications;
using GnaService.Infrastructure.Persistence;
using GnaService.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Contracts;
using Shared.Application.Messaging.Outbox;
using Shared.Domain.Notifications;
using Shared.Infrastructure.Persistence.Outbox;

namespace Backend.IntegrationTests.Persistence;

public sealed class NotificationOutboxDispatcherTests
{
    [Fact]
    public async Task HostedWorkerDrainsPendingDeliveriesBeforePollingAgain()
    {
        await using var database = new SqliteTestDatabase();
        var clock = new OutboxPollingTimeProvider();
        var sender = new RecordingSender();
        await using var provider = BuildServices(database, sender, clock);
        await SeedAsync(provider, Enumerable.Range(0, 151).Select(_ => Stage(Guid.NewGuid())));
        using var worker = ActivatorUtilities.CreateInstance<NotificationOutboxDispatcher>(provider, TimeSpan.FromHours(1));

        await worker.StartAsync(default);
        try
        {
            await clock.WaitingForPoll.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(151, sender.Delivered.Count);
        }
        finally { await worker.StopAsync(default); }
        await using var verification = provider.CreateAsyncScope();
        Assert.Equal(151, await verification.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Outbox
            .CountAsync(row => row.PublishedAtUtc != null));
    }

    [Fact]
    public async Task DispatcherFetchesAtMostFiftyPendingDeliveriesPerCall()
    {
        await using var database = new SqliteTestDatabase();
        var sender = new RecordingSender();
        await using var provider = BuildServices(database, sender);
        await SeedAsync(provider, Enumerable.Range(0, 51).Select(_ => Stage(Guid.NewGuid())));
        using var worker = ActivatorUtilities.CreateInstance<NotificationOutboxDispatcher>(provider);

        await worker.DispatchPendingAsync(default);

        Assert.Equal(50, sender.Delivered.Count);
        await worker.DispatchPendingAsync(default);
        Assert.Equal(51, sender.Delivered.Count);
        Assert.Equal(51, sender.Delivered.Distinct().Count());
    }

    [Fact]
    public async Task DeliveryFailuresBackOffAndLeavePoisonedRowsPending()
    {
        await using var database = new SqliteTestDatabase();
        var clock = new OutboxPollingTimeProvider();
        var sender = new RecordingSender();
        await using var provider = BuildServices(database, sender, clock);
        await SeedAsync(provider, Enumerable.Range(0, 51).Select(_ => Stage(Guid.NewGuid(), malformed: true)));
        using var worker = ActivatorUtilities.CreateInstance<NotificationOutboxDispatcher>(provider, TimeSpan.FromHours(1));

        await worker.StartAsync(default);
        try { await clock.WaitingForPoll.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { await worker.StopAsync(default); }

        Assert.Empty(sender.Delivered);
        await using var verification = provider.CreateAsyncScope();
        var rows = await verification.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Outbox.ToArrayAsync();
        Assert.All(rows, row => Assert.Null(row.PublishedAtUtc));
        Assert.Equal(50, rows.Count(row => row.LastAttemptAtUtc != null));
    }

    [Fact]
    public async Task CancellationPreservesEarlierDeliveriesAndLeavesInterruptedRowPending()
    {
        await using var database = new SqliteTestDatabase();
        using var cancellation = new CancellationTokenSource();
        var first = Stage(Guid.NewGuid());
        var second = Stage(Guid.NewGuid(), createdAt: first.CreatedAtUtc.AddSeconds(1));
        var sender = new RecordingSender { BlockedNotificationId = second.MessageId };
        await using var provider = BuildServices(database, sender);
        await SeedAsync(provider, [first, second]);
        using var worker = ActivatorUtilities.CreateInstance<NotificationOutboxDispatcher>(provider);

        var dispatch = worker.DispatchPendingAsync(cancellation.Token);
        try
        {
            await sender.DeliveryBlocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                await using var check = provider.CreateAsyncScope();
                var persisted = await check.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Outbox
                    .AnyAsync(row => row.Id == first.Id && row.PublishedAtUtc != null, deadline.Token);
                if (persisted) break;
                await Task.Delay(10, deadline.Token);
            }
        }
        finally
        {
            cancellation.Cancel();
            try { await dispatch; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatch);

        Assert.Equal(first.MessageId, Assert.Single(sender.Delivered));
        await using var verification = provider.CreateAsyncScope();
        var rows = await verification.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Outbox.ToArrayAsync();
        Assert.NotNull(rows.Single(row => row.Id == first.Id).PublishedAtUtc);
        var pending = rows.Single(row => row.Id == second.Id);
        Assert.Null(pending.PublishedAtUtc);
        Assert.Null(pending.LastAttemptAtUtc);
    }

    [Fact]
    public async Task FailedRowsMoveBehindUnattemptedValidMessages()
    {
        await using var database = new SqliteTestDatabase();
        var sender = new RecordingSender();
        await using var provider = BuildServices(database, sender);
        var now = DateTimeOffset.UtcNow;
        var valid = Stage(Guid.NewGuid(), createdAt: now.AddSeconds(-30));
        await SeedAsync(provider, Enumerable.Range(0, 50)
            .Select(_ => Stage(Guid.NewGuid(), malformed: true, createdAt: now.AddMinutes(-1)))
            .Append(valid));
        using var worker = ActivatorUtilities.CreateInstance<NotificationOutboxDispatcher>(provider);

        await worker.DispatchPendingAsync(default);
        Assert.Empty(sender.Delivered);
        await worker.DispatchPendingAsync(default);

        Assert.Equal(valid.MessageId, Assert.Single(sender.Delivered));
        await using var verification = provider.CreateAsyncScope();
        var rows = await verification.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Outbox.ToArrayAsync();
        Assert.NotNull(rows.Single(row => row.Id == valid.Id).PublishedAtUtc);
        Assert.All(rows.Where(row => row.Id != valid.Id), row => Assert.Null(row.PublishedAtUtc));
    }

    [Fact]
    public async Task TransportFailureRemainsPendingAndRetriesWithTheSameDeliveryId()
    {
        await using var database = new SqliteTestDatabase();
        var sender = new RecordingSender { FailuresRemaining = 1 };
        await using var provider = BuildServices(database, sender);
        var staged = Stage(Guid.NewGuid());
        await SeedAsync(provider, [staged]);
        using var worker = ActivatorUtilities.CreateInstance<NotificationOutboxDispatcher>(provider);

        await worker.DispatchPendingAsync(default);

        Assert.Empty(sender.Delivered);
        await using (var verification = provider.CreateAsyncScope())
        {
            var pending = await verification.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Outbox.SingleAsync();
            Assert.Null(pending.PublishedAtUtc);
            Assert.NotNull(pending.LastAttemptAtUtc);
        }

        await worker.DispatchPendingAsync(default);
        await worker.DispatchPendingAsync(default);

        Assert.Equal(staged.MessageId, Assert.Single(sender.Delivered));
        await using var completed = provider.CreateAsyncScope();
        Assert.NotNull((await completed.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Outbox.SingleAsync()).PublishedAtUtc);
    }

    [Fact]
    public async Task UnavailableRealtimeChannelIsTerminalAndDoesNotReplayWhenTheUserReconnects()
    {
        await using var database = new SqliteTestDatabase();
        var sender = new RecordingSender { Available = false };
        await using var provider = BuildServices(database, sender);
        await SeedAsync(provider, [Stage(Guid.NewGuid())]);
        using var worker = ActivatorUtilities.CreateInstance<NotificationOutboxDispatcher>(provider);

        await worker.DispatchPendingAsync(default);
        sender.Available = true;
        await worker.DispatchPendingAsync(default);

        Assert.Empty(sender.Delivered);
        Assert.Equal(1, sender.Attempts);
        await using var verification = provider.CreateAsyncScope();
        Assert.NotNull((await verification.ServiceProvider.GetRequiredService<GnaNotificationDbContext>().Outbox.SingleAsync()).PublishedAtUtc);
    }

    private static OutboxMessage Stage(Guid id, bool malformed = false, DateTimeOffset? createdAt = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        // An incompatible JSON token fails deserialization in the outbox worker itself.
        // Missing fields would deserialize to defaults rather than exercise invalid persisted JSON.
        object payload = malformed ? "not a notification object" :
            new NotificationDeliveryRequestedMessage(id, "student", "Queue changed", now,
                DeliveryType: NotificationDeliveryType.RealtimeOnly);
        return OutboxMessage.Create(new OutgoingMessage(id, payload, MessageRoutes.Notifications,
            MessageRoutes.NotificationDeliveryRequested), now);
    }

    private static async Task SeedAsync(ServiceProvider provider, IEnumerable<OutboxMessage> rows)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GnaNotificationDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Outbox.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static ServiceProvider BuildServices(SqliteTestDatabase database, RecordingSender sender,
        TimeProvider? clock = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock ?? TimeProvider.System);
        services.AddSingleton<IRealtimeNotificationSender>(sender);
        services.AddSingleton<IPushNotificationSender>(sender);
        services.AddScoped<NotificationDispatcher>();
        database.Register<GnaNotificationDbContext>(services);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingSender : IRealtimeNotificationSender, IPushNotificationSender
    {
        public ConcurrentQueue<Guid> Delivered { get; } = new();
        public Guid? BlockedNotificationId { get; init; }
        public TaskCompletionSource DeliveryBlocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int FailuresRemaining { get; set; }
        public bool Available { get; set; } = true;
        private int attempts;
        public int Attempts => Volatile.Read(ref attempts);

        public async Task<bool> TrySendAsync(NotificationDeliveryRequestedMessage notification, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref attempts);
            if (notification.NotificationId == BlockedNotificationId)
            {
                DeliveryBlocked.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (notification.NotificationId == Guid.Empty || notification.UserId is null)
                throw new InvalidDataException("Invalid notification payload.");
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new IOException("SignalR transport unavailable.");
            }
            if (!Available) return false;
            Delivered.Enqueue(notification.NotificationId);
            return true;
        }
    }

}
