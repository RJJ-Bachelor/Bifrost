namespace Shared.Application.Messaging.Contracts;

public sealed record GeneralizedCreatedMessage(string Message, Guid? SessionId = null, int? Generation = null);
