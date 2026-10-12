namespace Shared.Domain.Primitives;

public abstract class Entity<TId> where TId : notnull
{
    protected Entity() { }
    protected Entity(TId id) => Id = id;

    public virtual TId Id { get; protected set; } = default!;
}
