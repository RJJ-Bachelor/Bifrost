namespace Shared.Application.Messaging.Contracts;

[MessageContract("request.created")]
public sealed record HelpRequestCreatedMessage(Guid HelpRequestId, Guid SessionId, int Generation, string Message);
