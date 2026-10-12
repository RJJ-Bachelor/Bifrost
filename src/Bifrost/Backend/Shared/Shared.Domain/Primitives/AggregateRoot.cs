using Shared.Domain.DomainEvents;

namespace Shared.Domain.Primitives;

public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot() { }
    protected AggregateRoot(TId id) : base(id) { }

    public IReadOnlyCollection<IDomainEvent> GetDomainEvents() => _domainEvents.ToArray();
    public void ClearDomainEvents() => _domainEvents.Clear();
    public void RemoveDomainEvents(IReadOnlySet<Guid> eventIds) => _domainEvents.RemoveAll(domainEvent => eventIds.Contains(domainEvent.Id));

    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }
}
