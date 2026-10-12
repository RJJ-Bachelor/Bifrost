using Shared.Domain.DomainEvents;

namespace Shared.Domain.Primitives;

public interface IAggregateRoot
{
    IReadOnlyCollection<IDomainEvent> GetDomainEvents();
    void ClearDomainEvents();
    void RemoveDomainEvents(IReadOnlySet<Guid> eventIds);
}
