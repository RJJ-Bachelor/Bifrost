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

    private static ServiceProvider BuildServices(HandlerGate gate)
    {
        var services = new ServiceCollection();
        services.AddSingleton(gate);
        services.AddMessaging(new List<Type> { typeof(GatedHandler), typeof(FailingHandler) });
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
