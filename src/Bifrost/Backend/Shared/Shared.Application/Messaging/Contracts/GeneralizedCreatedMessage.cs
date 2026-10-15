namespace Shared.Application.Messaging.Contracts;

public sealed record GeneralizedCreatedMessage(Guid HelpRequestId, Guid SessionId, int Generation, string Message);
