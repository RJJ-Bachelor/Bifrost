namespace Shared.Application.Messaging.Contracts;

public sealed record SessionUpdatedIntegrationEvent(
    Guid EventId,
    Guid SessionId,
    int Generation,
    SessionUpdateType EventType,
    DateTimeOffset UpdatedAtUtc,
    int? PreviousGeneration = null);
