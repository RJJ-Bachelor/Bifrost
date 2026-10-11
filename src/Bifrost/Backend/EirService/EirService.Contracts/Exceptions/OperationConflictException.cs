namespace EirService.Contracts.Exceptions;

public sealed class OperationConflictException(string message) : Exception(message);
