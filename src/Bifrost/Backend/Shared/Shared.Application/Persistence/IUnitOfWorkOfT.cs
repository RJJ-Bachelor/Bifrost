namespace Shared.Application.Persistence;

// The owner keeps units of work for different modules separate in dependency injection.
public interface IUnitOfWork<TOwner> : IUnitOfWork { }
