namespace Shared.Application.Messaging.Contracts;

public sealed record HelpRequestCreatedMessage(string Message, Guid SessionId, int Generation);
