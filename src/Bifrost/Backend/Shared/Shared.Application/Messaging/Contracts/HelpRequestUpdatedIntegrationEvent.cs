namespace Shared.Application.Messaging.Contracts;

[MessageContract("request.updated")]
public sealed record HelpRequestUpdatedIntegrationEvent(
    Guid EventId, Guid HelpRequestId, Guid SessionId, int Generation, string? StudentId,
    string Change, string Status, string HelpType, string? AssignedTeacherId,
    DateTimeOffset QueuedAt, DateTimeOffset OccurredAt);

[MessageContract("request.answered")]
public sealed record HelpRequestAnsweredMessage(
    Guid HelpRequestId, Guid SessionId, int Generation, string Question, string Answer);
