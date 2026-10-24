using Shared.Domain.DomainEvents;

namespace EirService.Sessions.Domain.DomainEvents;

public sealed record SessionArchivedDomainEvent(
    Guid SessionId, int PreviousGeneration, int CurrentGeneration, DateTimeOffset ArchivedAtUtc)
    : DomainEvent(Guid.NewGuid());
