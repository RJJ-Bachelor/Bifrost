namespace Shared.Application.Messaging.Contracts;

public sealed record GeneralizedCreatedMessage(
    Guid GeneralizationId, Guid SessionId, int Generation, string Question, Guid[] HelpRequestIds);
