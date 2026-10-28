using Shared.Domain.DomainEvents;

namespace EirService.Sessions.Domain.DomainEvents;

public sealed record SessionRenamedDomainEvent(Guid SessionId, int Generation)
    : DomainEvent(Guid.NewGuid());
