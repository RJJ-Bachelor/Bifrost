using System.Collections.Concurrent;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Shared.Application.DomainEvents;
using Shared.Application.Messaging.Outbox;
using Shared.Application.Persistence;
using Shared.Domain.Primitives;
using Shared.Infrastructure.Persistence.Outbox;

namespace Shared.Infrastructure.Persistence.UnitOfWork;

public sealed class UnitOfWork<TOwner, TDbContext>(TDbContext dbContext, TimeProvider timeProvider)
    : IUnitOfWork<TOwner> where TDbContext : DbContext
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dispatchedIds = new HashSet<Guid>();
        var aggregates = new HashSet<IAggregateRoot>();
        var outgoing = new ConcurrentQueue<OutgoingMessage>();
        while (true)
        {
            var tracked = dbContext.ChangeTracker.Entries().Select(entry => entry.Entity).OfType<IAggregateRoot>().ToArray();
            aggregates.UnionWith(tracked);
            var events = tracked.SelectMany(aggregate => aggregate.GetDomainEvents())
                .Where(domainEvent => !dispatchedIds.Contains(domainEvent.Id)).ToArray();
            if (events.Length == 0) break;
            foreach (var domainEvent in events)
            {
                var notificationType = typeof(DomainEventNotification<>).MakeGenericType(domainEvent.GetType());
                var notification = (IEvent)Activator.CreateInstance(notificationType, domainEvent,
                    (Action<OutgoingMessage>)outgoing.Enqueue)!;
                await notification.PublishAsync(Mode.WaitForAll, cancellationToken);
                dispatchedIds.Add(domainEvent.Id);
            }
        }

        var now = timeProvider.GetUtcNow();
        var outboxMessages = outgoing.Select(message => OutboxMessage.Create(message, now)).ToArray();
        if (outboxMessages.Length > 0) dbContext.Set<OutboxMessage>().AddRange(outboxMessages);
        try
        {
            // Domain changes and promoted integration events are committed atomically.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Leave domain events pending and detach only the messages staged by this attempt.
            foreach (var message in outboxMessages) dbContext.Entry(message).State = EntityState.Detached;
            if (exception is DbUpdateConcurrencyException) throw new UnitOfWorkConcurrencyException(exception);
            throw;
        }
        foreach (var aggregate in aggregates) aggregate.RemoveDomainEvents(dispatchedIds);
    }
}
