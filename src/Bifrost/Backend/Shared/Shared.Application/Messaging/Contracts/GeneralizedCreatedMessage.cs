namespace Shared.Application.Messaging.Contracts;

// Sent for both pair and join. A join reuses the existing GeneralizationId and Question and lists all linked help requests.
public sealed record GeneralizedCreatedMessage(
    Guid GeneralizationId, Guid SessionId, int Generation, string Question, Guid[] HelpRequestIds);
