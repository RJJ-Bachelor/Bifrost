namespace Shared.Domain.DomainEvents;

public interface IDomainEvent
{
    Guid Id { get; }
}
