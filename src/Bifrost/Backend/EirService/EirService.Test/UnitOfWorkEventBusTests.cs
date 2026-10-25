using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.DomainEvents;
using Shared.Application.Messaging.Outbox;
using Shared.Domain.DomainEvents;
using Shared.Domain.Primitives;
using Shared.Infrastructure.Persistence.Outbox;
using Shared.Infrastructure.Persistence.UnitOfWork;

namespace EirService.Test;

[Collection("FastEndpoints")]
public sealed class UnitOfWorkEventBusTests
{
    [Fact]
    public async Task CommitWaitsForAllDomainEventHandlersBeforeSaving()
    {
        var gate = new HandlerGate();
        await using var services = BuildServices(gate);
        await using var db = CreateContext();
        var aggregate = new TestAggregate();
        db.Add(aggregate);
        aggregate.AddEvent(new GatedDomainEvent());
        var unitOfWork = new UnitOfWork<UnitOfWorkEventBusTests, RecordingDbContext>(db, TimeProvider.System);
        var commit = unitOfWork.SaveChangesAsync();
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, db.SaveCalls);
        Assert.Single(aggregate.GetDomainEvents());
        gate.Release.SetResult();
        await commit;
        Assert.Equal(1, db.SaveCalls);
        Assert.Empty(aggregate.GetDomainEvents());
        Assert.Single(db.ChangeTracker.Entries<OutboxMessage>());
    }

    [Fact]
    public async Task HandlerFailureLeavesDomainEventPendingAndDoesNotCommitPartiallyPromotedMessages()
    {
        await using var services = BuildServices(new HandlerGate());
        await using var db = CreateContext();
        var aggregate = new TestAggregate();
        db.Add(aggregate);
        aggregate.AddEvent(new FailingDomainEvent());
        var unitOfWork = new UnitOfWork<UnitOfWorkEventBusTests, RecordingDbContext>(db, TimeProvider.System);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.SaveChangesAsync());
        Assert.Equal(0, db.SaveCalls);
        Assert.Single(aggregate.GetDomainEvents());
        Assert.Empty(db.ChangeTracker.Entries<OutboxMessage>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitCoalescesExplicitKeysAndPrefersPersonalNotificationsRegardlessOfEventOrder(bool personalFirst)
    {
        await using var services = BuildServices(new HandlerGate());
        await using var db = CreateContext();
        var aggregate = new TestAggregate();
        db.Add(aggregate);
        var personal = new KeyedDomainEvent("session-a:user-a", 1, "Personal answer");
        var generic = new KeyedDomainEvent("session-a:user-a", 0, "Refresh queue");
        aggregate.AddEvent(personalFirst ? personal : generic);
        aggregate.AddEvent(personalFirst ? generic : personal);
        aggregate.AddEvent(new KeyedDomainEvent("session-a:user-a", 0, "Another source changed"));
        aggregate.AddEvent(new KeyedDomainEvent("session-a:user-b", 0, "Another participant"));
        aggregate.AddEvent(new KeyedDomainEvent("session-b:user-a", 0, "Another session"));
        aggregate.AddEvent(new KeyedDomainEvent(null, 0, "Request integration event"));
        aggregate.AddEvent(new KeyedDomainEvent(null, 0, "Answer integration event"));

        await new UnitOfWork<UnitOfWorkEventBusTests, RecordingDbContext>(db, TimeProvider.System).SaveChangesAsync();

        var messages = db.ChangeTracker.Entries<OutboxMessage>().Select(entry => entry.Entity).ToArray();
        Assert.Equal(5, messages.Length);
        Assert.Single(messages, message => message.Payload.Contains("Personal answer"));
        Assert.DoesNotContain(messages, message => message.Payload.Contains("Refresh queue") || message.Payload.Contains("Another source changed"));
        Assert.Single(messages, message => message.Payload.Contains("Another participant"));
        Assert.Single(messages, message => message.Payload.Contains("Another session"));
        Assert.Single(messages, message => message.Payload.Contains("Request integration event"));
        Assert.Single(messages, message => message.Payload.Contains("Answer integration event"));
        Assert.Empty(aggregate.GetDomainEvents());
    }

    private static ServiceProvider BuildServices(HandlerGate gate)
    {
        var services = new ServiceCollection();
        services.AddSingleton(gate);
        services.AddMessaging(new List<Type> { typeof(GatedHandler), typeof(FailingHandler), typeof(KeyedHandler) });
        var provider = services.BuildServiceProvider();
        provider.UseMessaging();
        return provider;
    }

    private static RecordingDbContext CreateContext() => new(
        new DbContextOptionsBuilder<RecordingDbContext>().UseNpgsql("Host=unused;Database=unused;Username=unused").Options);

    private sealed class RecordingDbContext(DbContextOptions<RecordingDbContext> options) : DbContext(options)
    {
        public int SaveCalls { get; private set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestAggregate>().HasKey(aggregate => aggregate.Id);
            modelBuilder.AddOutbox();
        }
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            ChangeTracker.AcceptAllChanges();
            return Task.FromResult(1);
        }
    }

    private sealed class TestAggregate : AggregateRoot<Guid>
    {
        public TestAggregate() : base(Guid.NewGuid()) { }
        public void AddEvent(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
    }

    private sealed record GatedDomainEvent() : DomainEvent(Guid.NewGuid());
    private sealed record FailingDomainEvent() : DomainEvent(Guid.NewGuid());

    private sealed record KeyedDomainEvent(string? Key, int Priority, string Value) : DomainEvent(Guid.NewGuid());

    private sealed class KeyedHandler : IEventHandler<DomainEventNotification<KeyedDomainEvent>>
    {
        public Task HandleAsync(DomainEventNotification<KeyedDomainEvent> notification, CancellationToken cancellationToken)
        {
            var change = notification.DomainEvent;
            // Independent integration events can share a correlation ID and must remain separate.
            notification.EnqueueIntegrationEvent(new OutgoingMessage(Guid.Parse("11111111-1111-1111-1111-111111111111"),
                new { change.Value }, "events", "changed", change.Key, change.Priority));
            return Task.CompletedTask;
        }
    }

    private sealed class HandlerGate
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class GatedHandler(HandlerGate gate) : IEventHandler<DomainEventNotification<GatedDomainEvent>>
    {
        public async Task HandleAsync(DomainEventNotification<GatedDomainEvent> notification, CancellationToken cancellationToken)
        {
            gate.Started.TrySetResult();
            await gate.Release.Task.WaitAsync(cancellationToken);
            notification.EnqueueIntegrationEvent(new OutgoingMessage(notification.DomainEvent.Id, new { Value = "Promoted" }, "events", "promoted"));
        }
    }

    private sealed class FailingHandler : IEventHandler<DomainEventNotification<FailingDomainEvent>>
    {
        public Task HandleAsync(DomainEventNotification<FailingDomainEvent> notification, CancellationToken cancellationToken)
        {
            notification.EnqueueIntegrationEvent(new OutgoingMessage(notification.DomainEvent.Id, new { Value = "Do not commit" }, "events", "failed"));
            throw new InvalidOperationException("Simulated domain handler failure");
        }
    }
}
