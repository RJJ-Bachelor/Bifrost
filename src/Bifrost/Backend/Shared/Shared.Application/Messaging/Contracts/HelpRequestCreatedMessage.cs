namespace Shared.Application.Messaging.Contracts;

public sealed record HelpRequestCreatedMessage(Guid HelpRequestId, Guid SessionId, int Generation, string Message);
