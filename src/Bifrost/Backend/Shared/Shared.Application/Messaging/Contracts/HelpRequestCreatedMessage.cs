namespace Shared.Application.Messaging.Contracts;

public sealed record HelpRequestCreatedMessage(string Message, Guid? SessionId = null, int? Generation = null);
