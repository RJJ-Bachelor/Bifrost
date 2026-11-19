using System.Data.Common;
using System.Text.Json;
using Backend.IntegrationTests.Infrastructure;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Application.Messaging;
using Shared.Application.Messaging.Outbox;
using Shared.Infrastructure.Persistence.Messaging;
using Shared.Infrastructure.Persistence.Outbox;

namespace Backend.IntegrationTests.Messaging;

public sealed class UnifiedMessageDeliveryTests
{
    [Theory]
    [InlineData("Local")]
    [InlineData("RabbitMq")]
    public async Task BothTransportsPreserveMetadataAndUseTheSameIdempotentReceiver(string transport)
    {
        await using var fixture = await Fixture.CreateAsync(transport);
        var cause = Guid.NewGuid();
        var outgoing = OutgoingMessage.ModuleEvent(cause, new Changed(fixture.CounterId), "test.changed", DateTimeOffset.UtcNow);
        await fixture.EnqueueAsync(outgoing);
        var subscriber = fixture.Services.GetServices<IHostedService>()
            .Single(service => service.GetType().Name.StartsWith("ModuleMessageSubscriber"));
        await subscriber.StartAsync(default);
        try
        {
            // StartAsync only schedules ExecuteAsync in .NET 10; wait for the actual subscription.
            if (transport == "RabbitMq")
                await fixture.Bus.Subscribed.WaitAsync(TimeSpan.FromSeconds(10));
            using var worker = ActivatorUtilities.CreateInstance<InternalOutboxDispatcher<DeliveryDb>>(fixture.Services);
            await worker.DispatchPendingAsync(default);
            if (transport == "RabbitMq")
            {
                var sent = Assert.Single(fixture.Bus.Sent);
                Assert.Equal(outgoing.Id, sent.Id);
                Assert.Equal(outgoing.Metadata, sent.Metadata);
                await fixture.Bus.DeliverAsync(sent);
                await fixture.Bus.DeliverAsync(sent);
            }
            else
            {
                Assert.Empty(fixture.Bus.Sent);
                await fixture.DeliverAsync(new(outgoing.Id, new(fixture.CounterId), outgoing.Metadata));
            }
        }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await subscriber.StopAsync(shutdown.Token);
        }
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDb>();
        Assert.Equal(1, (await db.Counters.SingleAsync()).Value);
        var receipt = Assert.Single(await db.Set<InboxMessage>().ToArrayAsync());
        Assert.NotNull(receipt.CompletedAtUtc);
        var reply = Assert.Single(await db.Set<OutboxMessage>().Where(row => row.RoutingKey == "test.result").ToArrayAsync());
        Assert.Equal(cause, reply.CorrelationId);
        Assert.Equal(outgoing.Id, reply.CausationId);
        Assert.NotNull((await db.Set<OutboxMessage>().SingleAsync(row => row.MessageId == outgoing.Id)).PublishedAtUtc);
    }

    [Fact]
    public async Task FailureAfterHandlerSaveRollsBackEffectsAndCompletionBeforeRetry()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.State.Fail = true;
        var message = new MessageEnvelope<Changed>(Guid.NewGuid(), new(fixture.CounterId));
        await Assert.ThrowsAsync<IOException>(() => fixture.DeliverAsync(message));
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DeliveryDb>();
            Assert.Equal(0, (await db.Counters.SingleAsync()).Value);
            Assert.Empty(await db.Set<OutboxMessage>().ToArrayAsync());
            Assert.Null((await db.Set<InboxMessage>().SingleAsync()).CompletedAtUtc);
        }
        fixture.State.Fail = false;
        await fixture.DeliverAsync(message);
        await fixture.DeliverAsync(message);
        await using var check = fixture.Services.CreateAsyncScope();
        var saved = check.ServiceProvider.GetRequiredService<DeliveryDb>();
        Assert.Equal(1, (await saved.Counters.SingleAsync()).Value);
        Assert.Single(await saved.Set<OutboxMessage>().ToArrayAsync());
        Assert.NotNull((await saved.Set<InboxMessage>().SingleAsync()).CompletedAtUtc);
    }

    [Fact]
    public async Task UnsupportedVersionAndMalformedPayloadAreParkedWithoutBusinessEffects()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.DeliverAsync(new(Guid.NewGuid(), new(fixture.CounterId),
            new("test.changed", 99, DateTimeOffset.UtcNow)));
        await using var scope = fixture.Services.CreateAsyncScope();
        var raw = scope.ServiceProvider.GetRequiredService<IRawMessageReceiver<Changed>>();
        await raw.ReceiveAsync(new(Guid.NewGuid(), JsonSerializer.SerializeToElement(new { counterId = "not-a-guid" })), default);
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDb>();
        var parked = await db.Set<InboxMessage>().ToArrayAsync();
        Assert.Equal(2, parked.Length);
        Assert.All(parked, row => { Assert.NotNull(row.DeadLetteredAtUtc); Assert.NotNull(row.LastError); });
        Assert.Equal(0, (await db.Counters.SingleAsync()).Value);
    }

    [Fact]
    public async Task RetryBudgetParksTheOriginalEnvelopeAndAllowsExplicitReplay()
    {
        await using var fixture = await Fixture.CreateAsync(maximumAttempts: 2);
        fixture.State.Fail = true;
        var message = new MessageEnvelope<Changed>(Guid.NewGuid(), new(fixture.CounterId));
        await Assert.ThrowsAsync<IOException>(() => fixture.DeliverAsync(message));
        await fixture.DeliverAsync(message);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DeliveryDb>();
            var parked = await db.Set<InboxMessage>().SingleAsync();
            Assert.Equal(2, parked.Attempts);
            Assert.NotNull(parked.DeadLetteredAtUtc);
            Assert.Contains(message.Id.ToString(), parked.Envelope);
            parked.Requeue();
            await db.SaveChangesAsync();
        }
        fixture.State.Fail = false;
        using var replay = ActivatorUtilities.CreateInstance<InboxReplayDispatcher<Changed, DeliveryDb>>(fixture.Services, "test-consumer");
        await replay.ReplayPendingAsync(default);
        await using var check = fixture.Services.CreateAsyncScope();
        Assert.NotNull((await check.ServiceProvider.GetRequiredService<DeliveryDb>().Set<InboxMessage>().SingleAsync()).CompletedAtUtc);
    }

    [Fact]
    public async Task ConsumersAndIndependentMessagesAreNotMistakenForDuplicates()
    {
        await using var fixture = await Fixture.CreateAsync();
        var message = new MessageEnvelope<Changed>(Guid.NewGuid(), new(fixture.CounterId));
        await fixture.DeliverAsync(message);
        await fixture.DeliverAsync(message with { Id = Guid.NewGuid() });
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDb>();
        var other = new InboxMessageReceiver<Changed, DeliveryDb>(db,
            scope.ServiceProvider.GetRequiredService<IMessageHandler<Changed>>(), TimeProvider.System,
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InboxMessageReceiver<Changed, DeliveryDb>>>(), "other-consumer");
        await other.ReceiveAsync(message, default);
        Assert.Equal(3, (await db.Counters.SingleAsync()).Value);
        Assert.Equal(3, await db.Set<InboxMessage>().CountAsync());
    }

    [Fact]
    public async Task ConcurrentDuplicateDeliveriesCommitOneEffect()
    {
        var transactions = new DuplicateTransactionObserver();
        await using var fixture = await Fixture.CreateAsync(transactions: transactions);
        fixture.State.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.State.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        transactions.Observe = true;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var message = new MessageEnvelope<Changed>(Guid.NewGuid(), new(fixture.CounterId));
        var deliveries = new List<Task>();
        try
        {
            // SQLite's async calls can block on locks, so each delivery needs its own thread.
            deliveries.Add(Task.Run(() => fixture.DeliverAsync(message, deadline.Token)));
            await fixture.State.Started.Task.WaitAsync(deadline.Token);
            deliveries.Add(Task.Run(() => fixture.DeliverAsync(message, deadline.Token)));
            // Keep the first transaction open until the duplicate has also attempted a claim.
            await transactions.DuplicateStarting.Task.WaitAsync(deadline.Token);
            fixture.State.Release.TrySetResult();
            await Task.WhenAll(deliveries).WaitAsync(deadline.Token);
        }
        finally
        {
            fixture.State.Release.TrySetResult();
            await deadline.CancelAsync();
            // Observe both deliveries before disposing their database, including on timeout.
            try { await Task.WhenAll(deliveries); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDb>();
        Assert.Equal(1, (await db.Counters.SingleAsync()).Value);
        Assert.Single(await db.Set<InboxMessage>().ToArrayAsync());
        Assert.Single(await db.Set<OutboxMessage>().ToArrayAsync());
    }

    [Fact]
    public async Task LegacyPersistedCommandIsConvertedAndProcessedThroughTheInbox()
    {
        await using var fixture = await Fixture.CreateAsync();
        var outgoing = OutgoingMessage.Internal(Guid.NewGuid(), new LegacyChanged(fixture.CounterId), "legacy.changed");
        await fixture.EnqueueAsync(outgoing);
        using var worker = ActivatorUtilities.CreateInstance<InternalOutboxDispatcher<DeliveryDb>>(fixture.Services);
        await worker.DispatchPendingAsync(default);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDb>();
        Assert.Equal(1, (await db.Counters.SingleAsync()).Value);
        Assert.Equal(outgoing.Id, (await db.Set<InboxMessage>().SingleAsync()).MessageId);
    }

    [Fact]
    public async Task ResumableWorkflowKeepsItsFirstCommitButDoesNotCompleteBeforeTheLastStep()
    {
        await using var fixture = await Fixture.CreateAsync(transactional: false);
        fixture.State.Fail = true;
        fixture.State.Resumable = true;
        var message = new MessageEnvelope<Changed>(Guid.NewGuid(), new(fixture.CounterId));
        await Assert.ThrowsAsync<IOException>(() => fixture.DeliverAsync(message));
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DeliveryDb>();
            Assert.Equal(1, (await db.Counters.SingleAsync()).Value);
            Assert.Null((await db.Set<InboxMessage>().SingleAsync()).CompletedAtUtc);
        }
        fixture.State.Fail = false;
        await fixture.DeliverAsync(message);
        await using var check = fixture.Services.CreateAsyncScope();
        Assert.Equal(1, (await check.ServiceProvider.GetRequiredService<DeliveryDb>().Counters.SingleAsync()).Value);
        Assert.NotNull((await check.ServiceProvider.GetRequiredService<DeliveryDb>().Set<InboxMessage>().SingleAsync()).CompletedAtUtc);
    }

    [Fact]
    public async Task CancellationRollsBackTheClaimAndBusinessChangesWithoutConsumingRetryBudget()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        fixture.State.Cancel = cancellation;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.DeliverAsync(
            new(Guid.NewGuid(), new(fixture.CounterId)), cancellation.Token));
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDb>();
        Assert.Equal(0, (await db.Counters.SingleAsync()).Value);
        Assert.Empty(await db.Set<InboxMessage>().ToArrayAsync());
    }

    [MessageContract("test.changed")]
    public sealed record Changed(Guid CounterId);
    public sealed record LegacyChanged(Guid CounterId) : ICommand;
    public sealed class Counter { public Guid Id { get; set; } public int Value { get; set; } }
    public sealed class State { public bool Fail; public bool Resumable; public CancellationTokenSource? Cancel; public TaskCompletionSource? Started; public TaskCompletionSource? Release; }
    public sealed class DeliveryDb(DbContextOptions<DeliveryDb> options) : DbContext(options)
    {
        public DbSet<Counter> Counters => Set<Counter>();
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Counter>().HasKey(counter => counter.Id);
            model.AddOutbox();
            model.AddInbox("delivery");
        }
    }
    public sealed class Handler(DeliveryDb db, State state) : IMessageHandler<Changed>
    {
        public async Task HandleAsync(MessageEnvelope<Changed> message, CancellationToken token)
        {
            var counter = await db.Counters.SingleAsync(row => row.Id == message.Payload.CounterId, token);
            if (!state.Resumable || counter.Value == 0)
            {
                counter.Value++;
                db.Set<OutboxMessage>().Add(OutboxMessage.Create(OutgoingMessage.Event(message.Id,
                    new { counter.Id }, "test", "test.result", DateTimeOffset.UtcNow), DateTimeOffset.UtcNow));
                await db.SaveChangesAsync(token);
            }
            state.Started?.TrySetResult();
            if (state.Release is not null) await state.Release.Task.WaitAsync(token);
            state.Cancel?.Cancel();
            token.ThrowIfCancellationRequested();
            if (state.Fail) throw new IOException("Injected failure after the handler save.");
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteTestDatabase database = new();
        public Guid CounterId { get; } = Guid.NewGuid();
        public State State { get; } = new();
        public Bus Bus { get; } = new();
        public ServiceProvider Services { get; private set; } = null!;
        public static async Task<Fixture> CreateAsync(string transport = "Local", int maximumAttempts = 8,
            bool transactional = true, DbTransactionInterceptor? transactions = null)
        {
            var fixture = new Fixture();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Messaging:ModuleRoutes:test.changed:Transport"] = transport }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(config);
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(fixture.State);
            services.AddSingleton<IMessageBus>(fixture.Bus);
            fixture.database.Register<DeliveryDb>(services, options =>
            {
                if (transactions is not null) options.AddInterceptors(transactions);
            });
            services.AddMessageReceiver<Changed, DeliveryDb, Handler>("test-consumer", transactional, maximumAttempts);
            services.AddModuleMessageRoute<Changed>("test.changed", "test-queue", "test", "test.changed", config);
            services.AddModuleMessageSubscriber<Changed>("test.changed");
            services.AddLegacyModuleCommand<LegacyChanged, Changed>("legacy.changed", old => new(old.CounterId));
            fixture.Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DeliveryDb>();
            await db.Database.EnsureCreatedAsync();
            db.Counters.Add(new Counter { Id = fixture.CounterId });
            await db.SaveChangesAsync();
            return fixture;
        }
        public async Task DeliverAsync(MessageEnvelope<Changed> message, CancellationToken token = default)
        {
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IMessageReceiver<Changed>>().ReceiveAsync(message, token);
        }
        public async Task EnqueueAsync(OutgoingMessage outgoing)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DeliveryDb>();
            db.Set<OutboxMessage>().Add(OutboxMessage.Create(outgoing, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync() { await Services.DisposeAsync(); await database.DisposeAsync(); }
    }
    private sealed class DuplicateTransactionObserver : DbTransactionInterceptor
    {
        private int starts;
        public bool Observe { get; set; }
        public TaskCompletionSource DuplicateStarting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (Observe && Interlocked.Increment(ref starts) == 2) DuplicateStarting.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
    private sealed class Bus : IMessageBus
    {
        public List<MessageEnvelope<object>> Sent { get; } = [];
        private readonly TaskCompletionSource<Func<MessageEnvelope<JsonElement>, CancellationToken, Task>> subscription =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Subscribed => subscription.Task;
        public Task PublishEnvelopeAsync(MessageEnvelope<object> message, string exchange, string route, CancellationToken token = default)
        { Sent.Add(message); return Task.CompletedTask; }
        public Task PublishAsync(Guid id, object payload, string exchange, string route, CancellationToken token = default, string? messageType = null) =>
            throw new InvalidOperationException("Rich envelopes must be preserved.");
        public Task<IAsyncDisposable> SubscribeAsync<T>(string queue, string exchange, string route,
            Func<MessageEnvelope<T>, CancellationToken, Task> handler, CancellationToken token = default)
        {
            subscription.SetResult((message, cancellation) => handler(new(message.Id,
                message.Payload.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!, message.Metadata), cancellation));
            return Task.FromResult<IAsyncDisposable>(new Subscription());
        }
        public async Task DeliverAsync(MessageEnvelope<object> message)
        {
            var receive = await subscription.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await receive(new(message.Id, JsonSerializer.SerializeToElement(message.Payload), message.Metadata), default);
        }
        private sealed class Subscription : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
}
