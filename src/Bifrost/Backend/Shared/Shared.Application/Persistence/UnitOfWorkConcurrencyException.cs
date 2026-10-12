namespace Shared.Application.Persistence;

public sealed class UnitOfWorkConcurrencyException(Exception innerException)
    : Exception("The data was changed by another operation.", innerException);
