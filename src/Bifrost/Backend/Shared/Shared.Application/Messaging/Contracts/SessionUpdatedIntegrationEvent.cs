namespace Shared.Application.Messaging.Contracts;

[MessageContract("session.updated")]
public sealed record SessionUpdatedIntegrationEvent(
    Guid EventId,
    Guid SessionId,
    int Generation,
    SessionUpdateType EventType,
    DateTimeOffset UpdatedAtUtc,
    int? PreviousGeneration = null);
