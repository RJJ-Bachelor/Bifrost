namespace Shared.Application.Messaging.Contracts;

public sealed record HelpRequestUpdatedIntegrationEvent(
    Guid EventId, Guid HelpRequestId, Guid SessionId, int Generation, string? StudentId,
    string Change, string Status, string HelpType, string? AssignedTeacherId,
    DateTimeOffset QueuedAt, DateTimeOffset OccurredAt);

public sealed record HelpRequestAnsweredMessage(
    Guid HelpRequestId, Guid SessionId, int Generation, string Question, string Answer);
